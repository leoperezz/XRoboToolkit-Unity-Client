using System.Collections;
using System.Collections.Generic;
using System.Net;
using Robot;
using Robot.Conf;
using Unity.XR.PICO.TOBSupport;
using Unity.XR.PXR;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class UIOperate : MonoBehaviour
{
    private const float CaptureControlsHeight = 28f;
    private const int CameraStreamPort = 63902;
    private const int CameraStreamWidth = 1280;
    private const int CameraStreamHeight = 480;
    private const int CameraStreamFps = 30;
    private const int CameraStreamBitrate = 5 * 1024 * 1024;

    public Text SN;
    public Text LocalIP;
    public Text TargetIP;
    public Text TrackNum;
    public Toggle HeadTog;
    public Toggle ControllerTog;
    public Toggle HandTrackingTog;
    public Toggle SendTog;
    public Toggle AcontrolerTog;
    public Dropdown bodyModeDrop;
    public TcpHandler TcpHandler;
    public Text BodyInfo;
    public Toggle HighAccuracy;
    public Text Version;
    public Button ReconnectBtn;
    public Toggle NetshareTog;

    public GameObject Simulator;
    public GameObject CameraObj;
    public GameObject IpInputDialog;
    public GameObject ExtDevPanel;
    public InputActionProperty SendDataAction;

    [Space(30)] [Header("Refactoring")] public VideoSourceManager videoSource;
    public VideoSourceConfigManager sourceConfig => videoSource.videoSourceConfigManager;

    public Dropdown videoSourceDropdown;
    private Toggle _cameraTog;
    private Toggle _audioTog;
    private bool _cameraStreamRequested;
    private Coroutine _cameraStartCoroutine;
    private Coroutine _cameraOpenTimeoutCoroutine;
    private bool _cameraPreviewReady;
    private AudioStreamSender _audioStreamSender;

    // Start is called before the first frame update
    private void Awake()
    {
#if UNITY_EDITOR
        if (Simulator != null)
        {
            Simulator.SetActive(true);
        }
#endif
        // ReconnectBtn.gameObject.SetActive(false);

        bodyModeDrop.onValueChanged.AddListener(OnBodyModeDrop);
        HeadTog.onValueChanged.AddListener(OnHeadTog);
        ControllerTog.onValueChanged.AddListener(OnControllerTog);
        HandTrackingTog.onValueChanged.AddListener(OnHandTrackingTog);
        CreateCaptureToggles();
        CameraHandle.CameraError += OnCameraError;

        SendTog.onValueChanged.AddListener(OnSendTog);
        Version.text = "v: " + Application.version;
        HighAccuracy.gameObject.SetActive(bodyModeDrop.value > 0);
        NetshareTog.onValueChanged.AddListener(OnNetShareTog);
        HighAccuracy.onValueChanged.AddListener(OnHighAccuracy);
        ReconnectBtn.onClick.AddListener(OnReconnectBtn);
        //The shared network function is only available on B-end devices.
        NetshareTog.gameObject.SetActive(false);
        // Bypass getting sn via enterprise service to enable data transport
        SetDeviceSN("TestDevice");
        bool intEnterprise = PXR_Enterprise.InitEnterpriseService();
        Debug.Log("---InitEnterpriseService :" + intEnterprise);
        PXR_Enterprise.BindEnterpriseService(OnBindEnterpriseService);

        // if (CameraObj != null)
        // {
        //     CameraObj.SetActive(false);
        // }

        AndroidProxy.CallBack += OnAndroidCallBack;
#if UNITY_EDITOR
        SetDeviceSN("TestDevice");
#endif
        // Refactoring
        sourceConfig.OnInitialized += OnSourceConfigOnOnInitialized;
        // Initialize video source configuration
        sourceConfig.Initialize();
    }

    private void OnSourceConfigOnOnInitialized()
    {
        // Update videoSourceDropdown options
        print("OnSourceConfigOnOnInitialized");
        videoSourceDropdown.ClearOptions();
        videoSourceDropdown.AddOptions(sourceConfig.GetVideoSourceNames());
    }

    private void OnAndroidCallBack(string key, string value)
    {
        if (key == "RequestPermissionsBack")
        {
            if (value == "0")
            {
                if (CameraObj != null)
                {
                    CameraObj.SetActive(true);
                }
            }
            else
            {
                Toast.Show("Permission denied!");
            }
        }
    }

    private void OnReconnectBtn()
    {
        TcpHandler.Reconnect();
    }

    public void TcpConnect(string ip)
    {
        TargetIP.text = "PC Service: " + ip;
        ReconnectBtn.gameObject.SetActive(true);
        TcpHandler.Connect(ip);
        ConnectSuccess();
    }

    public void ConnectSuccess()
    {
        TargetIP.text = "PC Service: " + TcpHandler.GetTargetIP;
    }

    private void OnBindEnterpriseService(bool bind)
    {
        Debug.Log("OnBindEnterpriseService " + bind);
        if (bind)
        {
            //The shared network function is only available on B-end devices.
            NetshareTog.gameObject.SetActive(true);
            PXR_Enterprise.GetSwitchSystemFunctionStatus(SystemFunctionSwitchEnum.SFS_USB_TETHERING,
                (value) => { NetshareTog.SetIsOnWithoutNotify(value == 1); });

            string sn = PXR_Enterprise.StateGetDeviceInfo(SystemInfoEnum.EQUIPMENT_SN);
            SetDeviceSN(sn);
        }
    }

    private void SetDeviceSN(string sn)
    {
        TcpHandler.SetDeviceSn(sn);
        Debug.Log("SN: " + sn);
        SN.text = "SN: " + sn;
    }

    private void OnNetShareTog(bool ison)
    {
        Debug.Log("OnNetShareTog:" + ison);
        if (ison)
            PXR_Enterprise.SwitchSystemFunction(SystemFunctionSwitchEnum.SFS_USB_TETHERING, SwitchEnum.S_ON);
        else
            PXR_Enterprise.SwitchSystemFunction(SystemFunctionSwitchEnum.SFS_USB_TETHERING, SwitchEnum.S_OFF);

        PXR_Enterprise.GetSwitchSystemFunctionStatus(SystemFunctionSwitchEnum.SFS_USB_TETHERING,
            (value) => { Debug.Log("SFS_USB_TETHERING:" + value); });
    }

    public void OnQuit()
    {
        Application.Quit();
    }

    public void OnExtraDevBtn()
    {
        ExtDevPanel.SetActive(true);
    }

    public void OnWriteIpBtn()
    {
        IpInputDialog.SetActive(true);
    }

    private void OnBodyModeDrop(int index)
    {
        TrackingData.TrackingType tType = (TrackingData.TrackingType)bodyModeDrop.value;
        int res = 0;
        bool support = false;

        MotionTrackerMode trackingMode = PXR_MotionTracking.GetMotionTrackerMode();
        if (tType == TrackingData.TrackingType.Body)
        {
            if (trackingMode != MotionTrackerMode.BodyTracking)
            {
                res = PXR_MotionTracking.CheckMotionTrackerModeAndNumber(MotionTrackerMode.BodyTracking,
                    MotionTrackerNum.TWO);
            }

            PXR_MotionTracking.GetBodyTrackingSupported(ref support);
        }
        else if (tType == TrackingData.TrackingType.Motion)
        {
            if (trackingMode != MotionTrackerMode.MotionTracking)
            {
                res = PXR_MotionTracking.CheckMotionTrackerModeAndNumber(MotionTrackerMode.MotionTracking,
                    MotionTrackerNum.ONE);
            }

            support = true;
        }

        if (!support || res != 0)
        {
            BodyInfo.text = "Tracker exception, please connect to calibrate tracker!";
            BodyInfo.color = Color.red;

            bodyModeDrop.SetValueWithoutNotify(0);
            // Update UI
            HighAccuracy.gameObject.SetActive(false);
            return;
        }
        
        // Update UI
        HighAccuracy.gameObject.SetActive(index > 0);

        BodyInfo.color = Color.white;
        BodyInfo.text = "Tracker detection is normal!";

        UpdateBodyTracking();
    }


    public void OnOpenCameraOperate()
    {
        if (CameraObj != null)
        {
            if (Permission.HasUserAuthorizedPermission(Permission.Camera) &&
                Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                CameraObj.SetActive(!CameraObj.activeSelf);
            }
            else if (!CameraObj.activeSelf)
            {
                var permissionCallbacks = new PermissionCallbacks();
                permissionCallbacks.PermissionGranted += PermissionGranted;
                permissionCallbacks.PermissionDenied += PermissionDenied;

                string[] permissions = { Permission.Camera, Permission.Microphone };
                Permission.RequestUserPermissions(permissions, permissionCallbacks);
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
            {
                Permission.RequestUserPermission(Permission.ExternalStorageRead);
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
            {
                Permission.RequestUserPermission(Permission.ExternalStorageWrite);
            }
        }
    }

    private void PermissionDenied(string obj)
    {
        Toast.Show("Permission denied!");
    }

    private void PermissionGranted(string obj)
    {
        if (CameraObj != null)
        {
            CameraObj.SetActive(true);
        }
    }

    private void RefreshLocalIP()
    {
        string localIP = Utils.GetLocalIPv4();
        LocalIP.text = localIP;
    }

    // Obtain the local IPv6 address
    private string GetLocalIPv6()
    {
        string localIP = "Not found";
        foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                localIP = ip.ToString();
                break;
            }
        }

        return localIP;
    }


    private void OnHeadTog(bool on)
    {
        TrackingData.SetHeadOn(on);
    }

    private void OnControllerTog(bool on)
    {
        TrackingData.SetControllerOn(on);
    }

    private void OnHandTrackingTog(bool on)
    {
        TrackingData.SetHandTrackingOn(on);
    }

    private void CreateCaptureToggles()
    {
        // Add one compact row to the existing vertical layout. Keeping both
        // capture controls in this row prevents them from consuming two rows.
        Transform trackingRows = HandTrackingTog.transform.parent.parent;
        var captureRow = new GameObject("CaptureControls", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        captureRow.layer = HandTrackingTog.gameObject.layer;
        captureRow.transform.SetParent(trackingRows, false);
        captureRow.transform.SetSiblingIndex(HandTrackingTog.transform.parent.GetSiblingIndex() + 1);

        // Toggles uses a VerticalLayoutGroup with childControlHeight disabled,
        // so its children must provide their own RectTransform height. Also add
        // a LayoutElement to keep the row correct if that setting changes later.
        RectTransform captureRect = captureRow.GetComponent<RectTransform>();
        captureRect.sizeDelta = new Vector2(0f, CaptureControlsHeight);
        var captureElement = captureRow.AddComponent<LayoutElement>();
        captureElement.minHeight = CaptureControlsHeight;
        captureElement.preferredHeight = CaptureControlsHeight;
        captureElement.flexibleHeight = 0f;

        var layout = captureRow.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 4, 0, 0);
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        GameObject cameraControl = Instantiate(HandTrackingTog.gameObject, captureRow.transform);
        cameraControl.name = "CameraStream";
        SetCaptureControlWidth(cameraControl, 132f);
        _cameraTog = cameraControl.GetComponent<Toggle>();
        _cameraTog.SetIsOnWithoutNotify(false);
        Text cameraLabel = cameraControl.GetComponentInChildren<Text>(true);
        if (cameraLabel != null) cameraLabel.text = "Front Camera";
        _cameraTog.onValueChanged.RemoveAllListeners();
        _cameraTog.onValueChanged.AddListener(OnCameraTog);

        GameObject audioControl = Instantiate(HandTrackingTog.gameObject, captureRow.transform);
        audioControl.name = "AudioStream";
        SetCaptureControlWidth(audioControl, 120f);
        _audioTog = audioControl.GetComponent<Toggle>();
        _audioTog.SetIsOnWithoutNotify(false);
        Text audioLabel = audioControl.GetComponentInChildren<Text>(true);
        if (audioLabel != null) audioLabel.text = "Micro";
        _audioTog.onValueChanged.RemoveAllListeners();
        _audioTog.onValueChanged.AddListener(OnAudioTog);
        _audioStreamSender = gameObject.AddComponent<AudioStreamSender>();

        // Apply the new row before the first rendered frame. The parent is a
        // VerticalLayoutGroup (not a GridLayoutGroup).
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)trackingRows);
    }

    private static void SetCaptureControlWidth(GameObject control, float width)
    {
        RectTransform rect = control.GetComponent<RectTransform>();
        if (rect != null) rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        LayoutElement element = control.GetComponent<LayoutElement>();
        if (element == null) element = control.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.flexibleWidth = 0f;
    }

    private void OnCameraTog(bool on)
    {
        _cameraStreamRequested = on;
        if (!on)
        {
            if (_cameraStartCoroutine != null)
            {
                StopCoroutine(_cameraStartCoroutine);
                _cameraStartCoroutine = null;
            }
            StopCameraOpenTimeout();
            _cameraPreviewReady = false;
            CameraHandle.StopPreview();
            CameraHandle.CloseCamera();
            Main.SetFrontCameraCaptureActive(false);
            Toast.Show("Camera stream stopped");
            return;
        }

        if (!Utils.IsPico4U())
        {
            Toast.Show("Front camera streaming requires a PICO 4 Ultra Enterprise device.");
            _cameraTog.SetIsOnWithoutNotify(false);
            _cameraStreamRequested = false;
            return;
        }
        if (string.IsNullOrEmpty(Robot.TcpHandler.GetTargetIP))
        {
            Toast.Show("Connect to PC Service before enabling Camera.");
            _cameraTog.SetIsOnWithoutNotify(false);
            _cameraStreamRequested = false;
            return;
        }

        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            StartCameraStreaming();
            return;
        }

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ =>
        {
            if (_cameraStreamRequested && _cameraTog.isOn) StartCameraStreaming();
        };
        callbacks.PermissionDenied += _ => DisableCameraToggle("Camera permission denied.");
        callbacks.PermissionDeniedAndDontAskAgain += _ =>
            DisableCameraToggle("Enable camera permission in PICO system settings.");
        Permission.RequestUserPermission(Permission.Camera, callbacks);
    }

    private void StartCameraStreaming()
    {
        if (!_cameraStreamRequested || !_cameraTog.isOn) return;
        if (_cameraStartCoroutine == null)
            _cameraStartCoroutine = StartCoroutine(StartCameraStreamingAfterVstRelease());
    }

    private IEnumerator StartCameraStreamingAfterVstRelease()
    {
        Main.SetFrontCameraCaptureActive(true);
        // VST releases the RGB camera asynchronously on the headset.
        yield return new WaitForSecondsRealtime(0.35f);
        _cameraStartCoroutine = null;
        if (!_cameraStreamRequested || !_cameraTog.isOn)
        {
            Main.SetFrontCameraCaptureActive(false);
            yield break;
        }

        Toast.Show("Starting front camera stream");
        _cameraPreviewReady = false;
        int result = CameraHandle.StartCameraPreview(
            CameraStreamWidth, CameraStreamHeight, CameraStreamFps, CameraStreamBitrate, 0,
            (int)PXRCaptureRenderMode.PXRCapture_RenderMode_3D,
            () =>
            {
                // Opening is asynchronous; honor a toggle-off that happened meanwhile.
                if (!_cameraStreamRequested) return;
                _cameraPreviewReady = true;
                StopCameraOpenTimeout();
                int sendResult = CameraHandle.StartSendImage(Robot.TcpHandler.GetTargetIP, CameraStreamPort);
                if (sendResult != 0)
                {
                    DisableCameraToggle("Unable to connect camera stream to PC Service (" + sendResult + ").");
                }
            });
        // CameraHandle.aar returns setCallback() here instead of the result of
        // openCameraAsync(). On current PICO 4 Ultra firmware, 1 means that the
        // callback was registered; actual success arrives through the state callback.
        if (result != 0 && result != 1)
        {
            DisableCameraToggle("Unable to open the front camera (" + result + ").");
            yield break;
        }
        Debug.Log("Front camera asynchronous open requested; callback result: " + result);
        if (!_cameraPreviewReady)
            _cameraOpenTimeoutCoroutine = StartCoroutine(CameraOpenTimeout());
    }

    private IEnumerator CameraOpenTimeout()
    {
        yield return new WaitForSecondsRealtime(5f);
        _cameraOpenTimeoutCoroutine = null;
        if (_cameraStreamRequested && !_cameraPreviewReady)
            DisableCameraToggle("Front camera did not reach preview state (timeout).");
    }

    private void StopCameraOpenTimeout()
    {
        if (_cameraOpenTimeoutCoroutine == null) return;
        StopCoroutine(_cameraOpenTimeoutCoroutine);
        _cameraOpenTimeoutCoroutine = null;
    }

    private void DisableCameraToggle(string message)
    {
        Toast.Show(message);
        _cameraStreamRequested = false;
        _cameraPreviewReady = false;
        StopCameraOpenTimeout();
        _cameraTog.SetIsOnWithoutNotify(false);
        CameraHandle.StopPreview();
        CameraHandle.CloseCamera();
        Main.SetFrontCameraCaptureActive(false);
    }

    private void OnCameraError(int errorCode)
    {
        if (_cameraStreamRequested)
            DisableCameraToggle("Front camera error (" + errorCode + ").");
    }

    private void OnAudioTog(bool on)
    {
        if (!on)
        {
            _audioStreamSender.StopStreaming();
            Toast.Show("Audio stream stopped");
            return;
        }
        if (string.IsNullOrEmpty(Robot.TcpHandler.GetTargetIP))
        {
            Toast.Show("Connect to PC Service before enabling Audio.");
            _audioTog.SetIsOnWithoutNotify(false);
            return;
        }
        if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            StartAudioStreaming();
            return;
        }

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => StartAudioStreaming();
        callbacks.PermissionDenied += _ =>
        {
            Toast.Show("Microphone permission denied.");
            _audioTog.SetIsOnWithoutNotify(false);
        };
        callbacks.PermissionDeniedAndDontAskAgain += _ =>
        {
            Toast.Show("Enable microphone permission in PICO system settings.");
            _audioTog.SetIsOnWithoutNotify(false);
        };
        Permission.RequestUserPermission(Permission.Microphone, callbacks);
    }

    private void StartAudioStreaming()
    {
        if (!_audioTog.isOn) return;
        if (_audioStreamSender.StartStreaming(Robot.TcpHandler.GetTargetIP))
            Toast.Show("PICO microphone streaming started");
        else
        {
            Toast.Show("Unable to start the PICO microphone.");
            _audioTog.SetIsOnWithoutNotify(false);
        }
    }

    private void OnSendTog(bool on)
    {
        TcpHandler.SendTrackingData = on;
        // Reset FPS
        if (!on)
        {
            FPSDisplay.Reset();
        }
    }

    private void OnHighAccuracy(bool on)
    {
        UpdateBodyTracking();
    }

    private void UpdateBodyTracking()
    {
        TrackingData.TrackingType tType = (TrackingData.TrackingType)bodyModeDrop.value;
        HighAccuracy.gameObject.SetActive(bodyModeDrop.value > 0);
        Debug.Log("UpdateBodyTracking " + tType);
        TrackNum.text = "";
        // Set bone length
        BodyTrackingBoneLength boneLength = new BodyTrackingBoneLength();
        if (bodyModeDrop.value <= 0)
        {
            int ret = PXR_MotionTracking.StopBodyTracking();
            BodyInfo.text = "BodyTracking close";
        }
        else
        {
            MotionTrackerConnectState state = new MotionTrackerConnectState();
            PXR_MotionTracking.GetMotionTrackerConnectStateWithSN(ref state);
            //  PXR_MotionTracking.GetMotionTrackerConnectStateWithSN(ref state);
            TrackNum.text = "Num: " + state.trackerSum;

            if (tType == TrackingData.TrackingType.Body)
            {
                BodyTrackingMode mode = BodyTrackingMode.BTM_FULL_BODY_LOW;
                if (HighAccuracy.isOn)
                {
                    mode = BodyTrackingMode.BTM_FULL_BODY_HIGH;
                }

                // Enable full body motion capture default mode
                int ret = PXR_MotionTracking.StartBodyTracking(mode, boneLength);
                BodyInfo.text = "Start BodyTracking " + ret;
                Debug.Log(" UpdateBodyTracking :" + ret + " trackerSum:" + state.trackerSum);
            }
            else if (tType == TrackingData.TrackingType.Motion)
            {
                BodyInfo.text = "Start MotionTracking";
            }
        }

        TrackingData.SetTrackingType(tType);
    }

    private float _lastTime = 0;

    // Update is called once per frame
    void Update()
    {
        if (TcpHandler.State != SocketState.WORKING)
        {
            if (Time.time - _lastTime > 2)
            {
                _lastTime = Time.time;
                RefreshLocalIP();
            }
        }

        if (AcontrolerTog != null && AcontrolerTog.isOn)
        {
            if (SendDataAction.action != null && SendDataAction.action.WasReleasedThisFrame())
            {
                SendTog.isOn = !SendTog.isOn;
                LogWindow.Info("Sending data: " + SendTog.isOn);
            }
        }
    }

    public void OnQuitBtn()
    {
        Application.Quit();
    }

    private void OnDestroy()
    {
        CameraHandle.CameraError -= OnCameraError;
        if (_cameraStreamRequested)
        {
            _cameraStreamRequested = false;
            CameraHandle.StopPreview();
            CameraHandle.CloseCamera();
            Main.SetFrontCameraCaptureActive(false);
        }
        if (_audioStreamSender != null) _audioStreamSender.StopStreaming();
    }
}
