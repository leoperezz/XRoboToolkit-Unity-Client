using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Robot
{
    /// <summary>
    /// Captures the default PICO microphone and sends PCM16 chunks on a socket
    /// that is completely independent from tracking and camera transport.
    /// Unity microphone APIs stay on the main thread; network I/O stays on its
    /// own worker and the bounded queue drops old audio instead of blocking.
    /// </summary>
    public sealed class AudioStreamSender : MonoBehaviour
    {
        public const int StreamPort = 63903;
        public const int SampleRate = 48000;
        public const int ChunkMilliseconds = 20;

        private const int ClipLengthSeconds = 1;
        private const int MaxQueuedChunks = 50;
        private const ushort ProtocolVersion = 1;
        private const ushort FormatPcmS16Le = 1;

        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private readonly AutoResetEvent _queueEvent = new AutoResetEvent(false);
        private readonly object _clientLock = new object();
        private AudioClip _microphoneClip;
        private string _targetIp;
        private Thread _sendThread;
        private volatile bool _running;
        private int _lastMicPosition;
        private int _channels = 1;
        private int _sampleRate = SampleRate;
        private long _sequence;
        private long _captureStartTimestampNs;
        private long _sampleFramesCaptured;
        private float[] _floatSamples;
        private TcpClient _activeClient;

        public bool IsStreaming => _running;

        public bool StartStreaming(string targetIp)
        {
            if (_running) return true;
            if (string.IsNullOrEmpty(targetIp)) return false;

            _microphoneClip = Microphone.Start(null, true, ClipLengthSeconds, SampleRate);
            if (_microphoneClip == null) return false;

            _targetIp = targetIp;
            _channels = Math.Max(1, _microphoneClip.channels);
            _sampleRate = Math.Max(1, _microphoneClip.frequency);
            _floatSamples = new float[(_sampleRate * ChunkMilliseconds / 1000) * _channels];
            _lastMicPosition = 0;
            _sequence = 0;
            _sampleFramesCaptured = 0;
            _captureStartTimestampNs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000L;
            while (_sendQueue.TryDequeue(out _)) { }

            _running = true;
            _sendThread = new Thread(SendLoop)
            {
                IsBackground = true,
                Name = "PICO microphone sender"
            };
            _sendThread.Start();
            return true;
        }

        public void StopStreaming()
        {
            if (!_running && _microphoneClip == null) return;
            _running = false;
            _queueEvent.Set();
            lock (_clientLock)
            {
                _activeClient?.Close();
            }
            if (_sendThread != null && _sendThread.IsAlive)
            {
                _sendThread.Join(1000);
            }
            _sendThread = null;
            if (Microphone.IsRecording(null)) Microphone.End(null);
            _microphoneClip = null;
            while (_sendQueue.TryDequeue(out _)) { }
        }

        private void Update()
        {
            if (!_running || _microphoneClip == null) return;
            int currentPosition = Microphone.GetPosition(null);
            if (currentPosition < 0 || currentPosition == _lastMicPosition) return;

            int availableFrames = currentPosition >= _lastMicPosition
                ? currentPosition - _lastMicPosition
                : _microphoneClip.samples - _lastMicPosition + currentPosition;
            int chunkFrames = _sampleRate * ChunkMilliseconds / 1000;
            while (availableFrames >= chunkFrames && _running)
            {
                if (!_microphoneClip.GetData(_floatSamples, _lastMicPosition)) break;
                long timestampNs = _captureStartTimestampNs +
                                   (_sampleFramesCaptured * 1000000000L / _sampleRate);
                EnqueuePacket(BuildPacket(_floatSamples, timestampNs, (ulong)++_sequence));
                _sampleFramesCaptured += chunkFrames;
                _lastMicPosition = (_lastMicPosition + chunkFrames) % _microphoneClip.samples;
                availableFrames -= chunkFrames;
            }
        }

        private void EnqueuePacket(byte[] packet)
        {
            while (_sendQueue.Count >= MaxQueuedChunks && _sendQueue.TryDequeue(out _)) { }
            _sendQueue.Enqueue(packet);
            _queueEvent.Set();
        }

        private byte[] BuildPacket(float[] samples, long timestampNs, ulong sequence)
        {
            byte[] packet = new byte[36 + samples.Length * 2];
            packet[0] = (byte)'X';
            packet[1] = (byte)'R';
            packet[2] = (byte)'A';
            packet[3] = (byte)'U';
            WriteBigEndian(packet, 4, ProtocolVersion);
            WriteBigEndian(packet, 6, FormatPcmS16Le);
            WriteBigEndian(packet, 8, (uint)_sampleRate);
            WriteBigEndian(packet, 12, (ushort)_channels);
            WriteBigEndian(packet, 14, (ushort)0);
            WriteBigEndian(packet, 16, (ulong)timestampNs);
            WriteBigEndian(packet, 24, sequence);
            WriteBigEndian(packet, 32, (uint)(samples.Length * 2));
            for (int i = 0; i < samples.Length; ++i)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short value = (short)Mathf.RoundToInt(clamped * 32767f);
                int offset = 36 + i * 2;
                packet[offset] = (byte)(value & 0xff);
                packet[offset + 1] = (byte)((value >> 8) & 0xff);
            }
            return packet;
        }

        private void SendLoop()
        {
            while (_running)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        lock (_clientLock) _activeClient = client;
                        client.NoDelay = true;
                        client.Connect(_targetIp, StreamPort);
                        using (NetworkStream stream = client.GetStream())
                        {
                            while (_running && client.Connected)
                            {
                                if (!_sendQueue.TryDequeue(out byte[] packet))
                                {
                                    _queueEvent.WaitOne(100);
                                    continue;
                                }
                                stream.Write(packet, 0, packet.Length);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Audio stream reconnect: " + exception.Message);
                    if (_running) Thread.Sleep(250);
                }
                finally
                {
                    lock (_clientLock) _activeClient = null;
                }
            }
        }

        private static void WriteBigEndian(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static void WriteBigEndian(byte[] buffer, int offset, ulong value)
        {
            WriteBigEndian(buffer, offset, (uint)(value >> 32));
            WriteBigEndian(buffer, offset + 4, (uint)value);
        }

        private void OnDestroy()
        {
            StopStreaming();
            _queueEvent.Dispose();
        }
    }
}
