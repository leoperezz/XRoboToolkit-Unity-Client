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
        CreateCameraToggle();
        CreateAudioToggle();

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

    private void CreateCameraToggle()
    {
        // Keep prefab compatibility: derive the new row from the existing Hand row.
        Transform trackingRows = HandTrackingTog.transform.parent.parent;
        GameObject cameraRow = Instantiate(HandTrackingTog.gameObject, trackingRows);
        cameraRow.name = "CameraStream";
        cameraRow.transform.SetSiblingIndex(HandTrackingTog.transform.parent.GetSiblingIndex() + 1);
        _cameraTog = cameraRow.GetComponent<Toggle>();
        _cameraTog.SetIsOnWithoutNotify(false);
        Text label = cameraRow.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = "Camera";
        }
        _cameraTog.onValueChanged.RemoveAllListeners();
        _cameraTog.onValueChanged.AddListener(OnCameraTog);

        // The parent uses a one-column GridLayoutGroup and was sized for its
        // original children, so reserve one more row without changing layout.
        RectTransform rows = trackingRows as RectTransform;
        if (rows != null)
        {
            rows.sizeDelta = new Vector2(rows.sizeDelta.x, rows.sizeDelta.y + 30f);
        }
    }

    private void OnCameraTog(bool on)
    {
        _cameraStreamRequested = on;
        if (!on)
        {
            CameraHandle.StopPreview();
            CameraHandle.CloseCamera();
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
        if (string.IsNullOrEmpty(global::TcpHandler.GetTargetIP))
        {
            Toast.Show("Connect to PC Service before enabling Camera.");
            _cameraTog.SetIsOnWithoutNotify(false);
            _cameraStreamRequested = false;
            return;
        }

        Toast.Show("Starting front camera stream");
        int result = CameraHandle.StartCameraPreview(
            CameraStreamWidth, CameraStreamHeight, CameraStreamFps, CameraStreamBitrate, 0,
            (int)PXRCaptureRenderMode.PXRCapture_RenderMode_3D,
            () =>
            {
                // Opening is asynchronous; honor a toggle-off that happened meanwhile.
                if (!_cameraStreamRequested) return;
                int sendResult = CameraHandle.StartSendImage(global::TcpHandler.GetTargetIP, CameraStreamPort);
                if (sendResult != 0)
                {
                    Toast.Show("Unable to connect camera stream to PC Service.");
                    _cameraTog.SetIsOnWithoutNotify(false);
                    _cameraStreamRequested = false;
                }
            });
        if (result != 0)
        {
            Toast.Show("Unable to open the front camera.");
            _cameraTog.SetIsOnWithoutNotify(false);
            _cameraStreamRequested = false;
        }
    }

    private void CreateAudioToggle()
    {
        Transform trackingRows = HandTrackingTog.transform.parent.parent;
        GameObject audioRow = Instantiate(HandTrackingTog.gameObject, trackingRows);
        audioRow.name = "AudioStream";
        audioRow.transform.SetSiblingIndex(_cameraTog.transform.GetSiblingIndex() + 1);
        _audioTog = audioRow.GetComponent<Toggle>();
        _audioTog.SetIsOnWithoutNotify(false);
        Text label = audioRow.GetComponentInChildren<Text>(true);
        if (label != null) label.text = "Audio to PC";
        _audioTog.onValueChanged.RemoveAllListeners();
        _audioTog.onValueChanged.AddListener(OnAudioTog);
        _audioStreamSender = gameObject.AddComponent<AudioStreamSender>();

        RectTransform rows = trackingRows as RectTransform;
        if (rows != null) rows.sizeDelta = new Vector2(rows.sizeDelta.x, rows.sizeDelta.y + 30f);
    }

    private void OnAudioTog(bool on)
    {
        if (!on)
        {
            _audioStreamSender.StopStreaming();
            Toast.Show("Audio stream stopped");
            return;
        }
        if (string.IsNullOrEmpty(global::TcpHandler.GetTargetIP))
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
        if (_audioStreamSender.StartStreaming(global::TcpHandler.GetTargetIP))
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
        if (_cameraStreamRequested)
        {
            _cameraStreamRequested = false;
            CameraHandle.StopPreview();
            CameraHandle.CloseCamera();
        }
        if (_audioStreamSender != null) _audioStreamSender.StopStreaming();
    }
}
