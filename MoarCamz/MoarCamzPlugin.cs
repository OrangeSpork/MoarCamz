using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using KKAPI;
using KKAPI.Utilities;
using Studio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static Studio.CameraControl;

namespace MoarCamz
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("StudioNEOV2")]
    [BepInProcess("CharaStudio")]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    [BepInDependency(ExtensibleSaveFormat.ExtendedSave.GUID)]
    public class MoarCamzPlugin : BaseUnityPlugin
    {
        public const string GUID = "orange.spork.moarcamzplugin";
        public const string PluginName = "MoarCamz";
        public const string Version = "1.0.5";

        public static MoarCamzPlugin Instance { get; private set; }

        internal BepInEx.Logging.ManualLogSource Log => Logger;

        private bool UIInit = false;

        public static ConfigEntry<float> FastDragPosStepper { get; set; }
        public static ConfigEntry<float> FastDragRotStepper { get; set; }
        public static ConfigEntry<float> SlowDragPosStepper { get; set; }
        public static ConfigEntry<float> SlowDragRotStepper { get; set; }
        public static ConfigEntry<KeyboardShortcut> NextCameraButton { get; set; }
        public static ConfigEntry<KeyboardShortcut> PrevCameraButton { get; set; }

        private static float FastDragPosIncrement => FastDragPosStepper.Value;
        private static float FastDragPosDecrement => FastDragPosStepper.Value * -1f;
        private static float SlowDragPosIncrement => SlowDragPosStepper.Value;
        private static float SlowDragPosDecrement => SlowDragPosStepper.Value * -1f;
        private static float FastDragRotIncrement => FastDragRotStepper.Value;
        private static float FastDragRotDecrement => FastDragRotStepper.Value * -1f;
        private static float SlowDragRotIncrement => SlowDragRotStepper.Value;
        private static float SlowDragRotDecrement => SlowDragRotStepper.Value * -1f;

        public List<MoarCamzData> MoarCamz { get; private set; } = new List<MoarCamzData>();
        public Transform CenterTarget { get; set; }
        public int CenterTargetKey { get; set; } = -1;
        public Vector3 PreviousPosition { get; set; }

        private Vector3 offsetPosition = Vector3.zero;
        public Vector3 OffsetPosition
        {
            get { return offsetPosition; }
            set { offsetPosition = value; }
        }
        private bool _lockOnEnabled;
        public bool LockOnEnabled
        {
            get { return _lockOnEnabled; }
            set {
                _lockOnEnabled = value;
                if (_lockOnEnabled)
                {
                    PreviousPosition = Studio.Studio.Instance.cameraCtrl.cameraData.pos;
                    lastFrameInit = false;
                    LockOnButton.image.color = Color.green;
                    ToggleCenterButton.image.color = Color.green;
                }
                else
                {
                    LockOnButton.image.color = Color.white;
                    ToggleCenterButton.image.color = Color.white;
                    SetPosition(PreviousPosition);
                }
            }
        }
        public int LastSelectedCamera {
            get { return lastSelectedCamera; }
            set { lastSelectedCamera = value; }
        }

        private bool _positionLocked;
        public bool PositionLocked
        {
            get
            {
                return _positionLocked;
            }
            set
            {
                _positionLocked = value;
                if (_positionLocked)
                {
                    PositionLockButton.image.color = Color.green;
                }
                else
                {
                    PositionLockButton.image.color = Color.white;
                }
            }
        }

        public MoarCamzPlugin()
        {
            if (Instance != null)
            {
                throw new InvalidOperationException("Singleton only.");
            }

            Instance = this;

            FastDragPosStepper = Config.Bind("Options", "Fast Drag Position Sensitivity", 1.0f);
            FastDragRotStepper = Config.Bind("Options", "Fast Drag Rotation Sensitivity", 3.0f);
            SlowDragPosStepper = Config.Bind("Options", "Slow Drag Position Sensitivity", 0.1f);
            SlowDragRotStepper = Config.Bind("Options", "Slow Drag Rotation Sensitivity", 1.0f);
            NextCameraButton = Config.Bind("Hotkeys", "Next Set Camera", new KeyboardShortcut(KeyCode.KeypadPlus));
            PrevCameraButton = Config.Bind("Hotkeys", "Prev Set Camera", new KeyboardShortcut(KeyCode.KeypadMinus));


            var harmony = new Harmony(GUID);
            harmony.Patch(typeof(StudioScene).GetMethod(nameof(StudioScene.OnClickLoadCamera)), null, new HarmonyMethod(typeof(MoarCamzPlugin).GetMethod(nameof(MoarCamzPlugin.OnClickLoadCameraPostfix), AccessTools.all)));
            harmony.Patch(typeof(StudioScene).GetMethod(nameof(StudioScene.OnClickSaveCamera)), null, new HarmonyMethod(typeof(MoarCamzPlugin).GetMethod(nameof(MoarCamzPlugin.OnClickSaveCameraPostfix), AccessTools.all)));
#if KKS
            harmony.Patch(AccessTools.Method(typeof(Studio.CameraControl), "LateUpdate"), null, new HarmonyMethod(typeof(MoarCamzPlugin).GetMethod(nameof(MoarCamzPlugin.CameraControlInternalUpdateCameraStatePostfix), AccessTools.all)));
#else
            harmony.Patch(AccessTools.Method(typeof(Studio.CameraControl), "InternalUpdateCameraState"), null, new HarmonyMethod(typeof(MoarCamzPlugin).GetMethod(nameof(MoarCamzPlugin.CameraControlInternalUpdateCameraStatePostfix), AccessTools.all)));
#endif

#if DEBUG
            Log.LogInfo("MoarCamz Loaded");
#endif
        }

        public void Awake()
        {
            KKAPI.Studio.StudioAPI.StudioLoadedChanged += StudioAPI_StudioLoadedChanged;
            KKAPI.Studio.SaveLoad.StudioSaveLoadApi.RegisterExtraBehaviour<MoarCamzStudioController>(GUID);
        }

        private void StudioAPI_StudioLoadedChanged(object sender, EventArgs e)
        {
            InitUI();
            Log.LogDebug("MoarCamz UI Loaded");
        }

        private GameObject cameraSlotFab;
        private ScrollRect cameraScroll;
        private GameObject blankCameraPrefab;

        private void InitUI()
        {
            // Add scroll
            RectTransform container = (RectTransform)GameObject.Find("StudioScene/Canvas System Menu/02_Camera").transform;
            cameraSlotFab = container.GetChild(0).gameObject;

            GameObject scrollView = DefaultControls.CreateScrollView(new DefaultControls.Resources());
            scrollView.name = "Scroll";
            scrollView.transform.SetParent(container, false);

            cameraScroll = scrollView.GetComponent<ScrollRect>();
            RectTransform cameraScrollRect = (RectTransform)cameraScroll.transform;
#if KKS            
            cameraScrollRect.offsetMin = new Vector2(-116f, -56f);
            cameraScrollRect.offsetMax = new Vector2(356f, -8f);
            cameraScrollRect.sizeDelta = new Vector2(472f, 48f);
#else
            cameraScrollRect.offsetMin = new Vector2(-96f, -56f);
            cameraScrollRect.offsetMax = new Vector2(336f, -8f);
#endif            

            cameraScroll.vertical = false;
            GameObject.Destroy(cameraScroll.GetComponent<Image>());
            GameObject.Destroy(cameraScroll.horizontalScrollbar.gameObject);
            GameObject.Destroy(cameraScroll.verticalScrollbar.gameObject);            
            cameraScroll.viewport.GetComponent<Image>().sprite = null;
            cameraScroll.scrollSensitivity *= -18f;
            cameraScroll.content.anchorMin = Vector2.zero;
            cameraScroll.content.anchorMax = new Vector2(0f, 1f);            
            for (int i = 0; i < 10; i++)
            {
                RectTransform defaultCam = (RectTransform)container.Find(i.ToString("00"));
                defaultCam.SetParent(cameraScroll.content, true);
                MoarCamzData moarCamzData = new MoarCamzData();
                moarCamzData.SlotNumber = i + 1;
                moarCamzData.CameraRect = defaultCam;
                moarCamzData.CameraImage = defaultCam.transform.Find("Button Load").GetComponent<Image>();
                MoarCamz.Add(moarCamzData);
            }

            ResizeCameraScroll();

            // Load Edit UI
            byte[] uiBundleBytes = ResourceUtils.GetEmbeddedResource("moarcamzui.unity3d");
            AssetBundle uiAssetBundle = AssetBundle.LoadFromMemory(uiBundleBytes);
            GameObject menu = GameObject.Find("/StudioScene/Canvas System Menu/02_Camera");

            blankCameraPrefab = uiAssetBundle.LoadAsset<GameObject>("assets/prefab/camloadimage.prefab");
            blankCameraPrefab.SetActive(false);
            
            ui = GameObject.Instantiate(uiAssetBundle.LoadAsset<GameObject>("assets/prefab/camerasettings.prefab"), menu.transform);
            ui.transform.localScale = new Vector3(0.65f, 0.65f, 0.65f);
            ui.transform.localPosition = new Vector3(135f, -110f);            
            ui.SetActive(false);

            ui.transform.Find("Position/PositionButton").GetComponent<Image>().color = Color.green;

            // Add side controls

            additionalButtons = GameObject.Instantiate(uiAssetBundle.LoadAsset<GameObject>("assets/prefab/moarcamzcontrols.prefab"), menu.transform);
            additionalButtons.transform.localScale = new Vector3(0.65f, 0.65f, 0.65f);
            additionalButtons.transform.localPosition = new Vector3(-115f, -20f);
            additionalButtons.SetActive(true);

            // Register References
            AddCamButton = additionalButtons.transform.Find("Add Cam Button").GetComponent<Button>();
            DelCamButton = additionalButtons.transform.Find("Del Cam Button").GetComponent<Button>();
            ToggleCenterButton = additionalButtons.transform.Find("Toggle Center").GetComponent<Button>();
            ToggleUIButton = additionalButtons.transform.Find("Toggle UI").GetComponent<Button>();

            PositionButton = ui.transform.Find("Position/PositionButton").GetComponent<Button>();
            RotationButton = ui.transform.Find("Rotation/RotationButton").GetComponent<Button>();
            DistanceButton = ui.transform.Find("Distance/DistanceButton").GetComponent<Button>();

            PositionX = ui.transform.Find("Position/PositionX").GetComponent<TMP_InputField>();
            PositionY = ui.transform.Find("Position/PositionY").GetComponent<TMP_InputField>();
            PositionZ = ui.transform.Find("Position/PositionZ").GetComponent<TMP_InputField>();

            RotationX = ui.transform.Find("Rotation/RotationX").GetComponent<TMP_InputField>();
            RotationY = ui.transform.Find("Rotation/RotationY").GetComponent<TMP_InputField>();
            RotationZ = ui.transform.Find("Rotation/RotationZ").GetComponent<TMP_InputField>();

            Distance = ui.transform.Find("Distance/Distance").GetComponent<TMP_InputField>();

            ScrollNav = ui.transform.Find("ScrollNav").gameObject;
            FastNav = ui.transform.Find("ScrollNav/FastNav").gameObject;
            SlowNav = ui.transform.Find("ScrollNav/SlowNav").gameObject;

            XZScroll = ui.transform.Find("ScrollNav/FastNav/XZScroll").GetComponent<Image>();
            XScroll = ui.transform.Find("ScrollNav/SlowNav/XScroll").GetComponent<Image>();
            ZScroll = ui.transform.Find("ScrollNav/SlowNav/ZScroll").GetComponent<Image>();
            YScroll = ui.transform.Find("ScrollNav/YScroll").GetComponent<Image>();            
            DScroll = ui.transform.Find("ScrollNav/DScroll").GetComponent<Image>();

            ScrollXZButton = ui.transform.Find("ScrollNav/FastNav/ScrollXZButton").GetComponent<Button>();
            ScrollXYButton = ui.transform.Find("ScrollNav/SlowNav/ScrollXYButton").GetComponent<Button>();
            ScrollY = ui.transform.Find("ScrollNav/ScrollY").GetComponent<TextMeshProUGUI>();

            LockOnButton = ui.transform.Find("AdditionalOptions/LockOnButton").GetComponent<Button>();
            SetCenterButton = ui.transform.Find("AdditionalOptions/SetCenterButton").GetComponent<Button>();
            ClearCenterButton = ui.transform.Find("AdditionalOptions/ClearCenterButton").GetComponent<Button>();
            PositionLockButton = ui.transform.Find("Distance/PositionLockButton").GetComponent<Button>();

            CenterTargetText = ui.transform.Find("AdditionalOptions/CenterTargeText").GetComponent<TextMeshProUGUI>();

            // Initialize handlers

            ScrollXZButton.onClick.AddListener(() => {
                FastNav.SetActive(false);
                SlowNav.SetActive(true);                
            });

            ScrollXYButton.onClick.AddListener(() => {
                FastNav.SetActive(true);
                SlowNav.SetActive(false);                               
            });

            PositionButton.onClick.AddListener(() => {
                PositionButton.GetComponent<Image>().color = Color.green;
                RotationButton.GetComponent<Image>().color = Color.white;
                positionSelected = true;

                ScrollXZButton.transform.Find("Text").GetComponent<Text>().text = "XZ";
                ScrollXYButton.transform.Find("Text").GetComponent<Text>().text = "X     Z";
                ScrollY.text = "Y";
                
                ZScroll.transform.SetParent(SlowNav.transform);
                ZScroll.GetComponent<RectTransform>().localPosition = new Vector3(-85, 315, 0);
                YScroll.transform.SetParent(ScrollNav.transform);
                YScroll.GetComponent<RectTransform>().localPosition = new Vector3(-40, 315, 0);
            });

            RotationButton.GetComponent<Button>().onClick.AddListener(() => {
                PositionButton.GetComponent<Image>().color = Color.white;
                RotationButton.GetComponent<Image>().color = Color.green;
                positionSelected = false;
                ScrollXZButton.transform.Find("Text").GetComponent<Text>().text = "XY";
                ScrollXYButton.transform.Find("Text").GetComponent<Text>().text = "X     Y";
                ScrollY.text = "Z";

                ZScroll.transform.SetParent(ScrollNav.transform);
                ZScroll.GetComponent<RectTransform>().localPosition = new Vector3(-40, 315, 0);
                YScroll.transform.SetParent(SlowNav.transform);
                YScroll.GetComponent<RectTransform>().localPosition = new Vector3(-85, 315, 0);
            });
            
            PositionX.onEndEdit.AddListener((s) => {
                if (float.TryParse(s, out float x))
                {                    
                    SetPositionX(x);
                    PositionX.m_isSelected = false;
                }
            });
            PositionY.onEndEdit.AddListener((s) => {
                if (float.TryParse(s, out float y))
                {
                    SetPositionY(y);
                    PositionY.m_isSelected = false;
                }
            });
            PositionZ.onEndEdit.AddListener((s) => {
                if (float.TryParse(s, out float z))
                {
                    SetPositionZ(z);
                    PositionZ.m_isSelected = false;
                }
            });

            RotationX.onEndEdit.AddListener((s) => {
                if (float.TryParse(s, out float x))
                {
                    SetRotationX(x);
                    RotationX.m_isSelected = false;
                }
            });
            RotationY.onEndEdit.AddListener((s) => {
                if (float.TryParse(s, out float y))
                {                    
                    SetRotationY(y);
                    RotationY.m_isSelected = false;
                }
            });
            RotationZ.onEndEdit.AddListener((s) => {
                if (float.TryParse(s, out float z))
                {                    
                    SetRotationZ(z);
                    RotationZ.m_isSelected = false;
                }
            });
            Distance.onEndEdit.AddListener((s) => {
                if (float.TryParse(s, out float d))
                {                    
                    SetDistance(d);
                    Distance.m_isSelected = false;
                }
            });

            EventTrigger.Entry xzDragEvent = new EventTrigger.Entry();
            xzDragEvent.eventID = EventTriggerType.Drag;
            xzDragEvent.callback.AddListener((data) => {
                PointerEventData pData = (PointerEventData)data;
                if (pData.delta.y > 0)
                {
                    if (positionSelected)
                        AddPositionZ(FastDragPosDecrement);
                    else
                        AddRotationX(FastDragRotIncrement);
                }
                else if (pData.delta.y < 0)
                {
                    if (positionSelected)
                        AddPositionZ(FastDragPosIncrement);
                    else
                        AddRotationX(FastDragRotDecrement);
                }

                if (pData.delta.x > 0)
                {
                    if (positionSelected)
                        AddPositionX(FastDragPosDecrement);
                    else
                        AddRotationY(FastDragRotDecrement);
                }
                else if (pData.delta.x < 0)
                {
                    if (positionSelected)
                        AddPositionX(FastDragPosIncrement);
                    else
                        AddRotationY(FastDragRotIncrement);
                }
                dragging = true;
            });
            XZScroll.gameObject.AddComponent<EventTrigger>().triggers.Add(xzDragEvent);

            EventTrigger.Entry xDragEvent = new EventTrigger.Entry();
            xDragEvent.eventID = EventTriggerType.Drag;
            xDragEvent.callback.AddListener((data) => {
                PointerEventData pData = (PointerEventData)data;
                if (positionSelected)
                {
                    if (pData.delta.y > 0)
                        AddPositionX(FastNav.activeSelf ? FastDragPosDecrement : SlowDragPosDecrement);
                    else if (pData.delta.y < 0)
                        AddPositionX(FastNav.activeSelf ? FastDragPosIncrement : SlowDragPosIncrement);
                }
                else
                {
                    if (pData.delta.y > 0)
                        AddRotationX(FastNav.activeSelf ? FastDragRotIncrement : SlowDragRotIncrement);
                    else if (pData.delta.y < 0)
                        AddRotationX(FastNav.activeSelf ? FastDragRotDecrement : SlowDragRotDecrement);
                }
                dragging = true;
            });
            XScroll.gameObject.AddComponent<EventTrigger>().triggers.Add(xDragEvent);

            EventTrigger.Entry yDragEvent = new EventTrigger.Entry();
            yDragEvent.eventID = EventTriggerType.Drag;
            yDragEvent.callback.AddListener((data) => {
                PointerEventData pData = (PointerEventData)data;
                if (positionSelected)
                {
                    if (pData.delta.y > 0)
                        AddPositionY(FastNav.activeSelf ? FastDragPosIncrement : SlowDragPosIncrement);
                    else if (pData.delta.y < 0)
                        AddPositionY(FastNav.activeSelf ? FastDragPosDecrement : SlowDragPosDecrement);
                }
                else
                {
                    if (pData.delta.y > 0)
                        AddRotationY(FastNav.activeSelf ? FastDragRotDecrement : SlowDragRotDecrement);
                    else if (pData.delta.y < 0)
                        AddRotationY(FastNav.activeSelf ? FastDragRotIncrement : SlowDragRotIncrement);
                }
                dragging = true;
            });
            YScroll.gameObject.AddComponent<EventTrigger>().triggers.Add(yDragEvent);

            EventTrigger.Entry zDragEvent = new EventTrigger.Entry();
            zDragEvent.eventID = EventTriggerType.Drag;
            zDragEvent.callback.AddListener((data) => {
                PointerEventData pData = (PointerEventData)data;
                if (positionSelected)
                {
                    if (pData.delta.y > 0)
                        AddPositionZ(FastNav.activeSelf ? FastDragPosDecrement : SlowDragPosDecrement);
                    else if (pData.delta.y < 0)
                        AddPositionZ(FastNav.activeSelf ? FastDragPosIncrement : SlowDragPosIncrement);
                }
                else
                {
                    if (pData.delta.y > 0)
                        AddRotationZ(FastNav.activeSelf ? FastDragRotIncrement : SlowDragRotIncrement);
                    else if (pData.delta.y < 0)
                        AddRotationZ(FastNav.activeSelf ? FastDragRotDecrement : SlowDragRotDecrement);
                }
                dragging = true;
            });
            ZScroll.gameObject.AddComponent<EventTrigger>().triggers.Add(zDragEvent);

            EventTrigger.Entry dDragEvent = new EventTrigger.Entry();
            dDragEvent.eventID = EventTriggerType.Drag;
            dDragEvent.callback.AddListener((data) => {
                PointerEventData pData = (PointerEventData)data;
                if (pData.delta.y > 0)
                    AddDistance(FastNav.activeSelf ? FastDragPosDecrement : SlowDragPosDecrement);
                else if (pData.delta.y < 0)
                    AddDistance(FastNav.activeSelf ? FastDragPosIncrement : SlowDragPosIncrement);
                dragging = true;
            });
            DScroll.gameObject.AddComponent<EventTrigger>().triggers.Add(dDragEvent);

            ToggleUIButton.onClick.AddListener(() => {
                ui.SetActive(!ui.activeSelf);
                ToggleUIButton.GetComponent<Image>().color = ui.activeSelf ? Color.green : Color.white;
            });

            SetCenterButton.onClick.AddListener(() =>
            {
                GuideObject go = GuideObjectManager.Instance.selectObject;
                if (go != null)
                {
#if DEBUG
                    Log.LogInfo($"Looking for name for GO: {go.name} Target: {go.transformTarget?.name} DicKey: {go.dicKey}");
#endif
                    if (Studio.Studio.Instance.dicObjectCtrl.TryGetValue(go.dicKey, out ObjectCtrlInfo oci))
                    {
#if DEBUG
                        Log.LogInfo($"Found OCI: {oci?.treeNodeObject?.textName}");
#endif
                        CenterTargetKey = go.dicKey;
                        SetCenterTarget(CenterTargetKey, null);
                    }
                    else
                    {
                        bool found = false;
#if DEBUG
                        Log.LogInfo($"Not Found in OCI dic, attempting to find parent: {go?.transformTarget?.parent}");
#endif
                        Transform parent = go?.transformTarget?.parent;
                        while (!found && parent != null)
                        {
                            foreach (int key in Studio.Studio.Instance.dicObjectCtrl.Keys)
                            {
                                if (Studio.Studio.Instance.dicObjectCtrl[key]?.guideObject?.transformTarget == parent)
                                {
#if DEBUG
                                    Log.LogInfo($"Found Parent: {Studio.Studio.Instance.dicObjectCtrl[key]?.treeNodeObject?.textName} Object: {Studio.Studio.Instance.dicObjectCtrl[key]?.guideObject?.transformTarget?.name} Key: {key}");
#endif

                                    CenterTargetKey = key;
                                    found = true;
                                    break;
                                }                                
                            }
                            if (!found)
                            {
                                parent = parent.parent;
                            }
                        }
                        if (found)
                            SetCenterTarget(CenterTargetKey, go.transformTarget.name);
                        else
                        {
#if DEBUG
                            Log.LogInfo($"No Suitable Center Target Found for GO");
#endif
                        }
                    }
                }
            });

            ClearCenterButton.onClick.AddListener(() => {
                CenterTarget = null;
                LockOnEnabled = false;
                PositionLocked = false;
                CenterTargetKey = -1;
                CenterTargetText.text = "Cntr: No center set.";
            });

            LockOnButton.onClick.AddListener(() => {
                LockOnEnabled = !LockOnEnabled;
                
            });

            ToggleCenterButton.onClick.AddListener(() => {
                LockOnEnabled = !LockOnEnabled;               
            });

            PositionLockButton.onClick.AddListener(() =>
            {
                PositionLocked = !PositionLocked;
            });

            AddCamButton.onClick.AddListener(() => {
                MoarCamzData moarCamzData = new MoarCamzData();
                moarCamzData.SlotNumber = 1 + MoarCamz.Count;
                MoarCamz.Add(moarCamzData);
                AddCameraSlot(moarCamzData.SlotNumber, moarCamzData);                                
#if DEBUG
                Log.LogInfo($"Added Camera Slot: {moarCamzData.SlotNumber} {moarCamzData.CameraRect} {moarCamzData.CameraImage}");
#endif
            });

            DelCamButton.onClick.AddListener(() =>
            {
                int lastSlot = MoarCamz.Count;
                DelCameraSlot(lastSlot);
#if DEBUG
                Log.LogInfo($"Removed Camera Slot: {lastSlot}");
#endif

            });

            DelCamButton.gameObject.SetActive(false);
            ToggleCenterButton.gameObject.SetActive(false);
            LockOnButton.interactable = false;
            ClearCenterButton.interactable = false;
            PositionLockButton.gameObject.SetActive(false);
            UIInit = true;
            uiAssetBundle.Unload(false);

        }

        public void SetCenterTarget(int centerTarget, string centerTargetBone)
        {
#if DEBUG
            Log.LogInfo($"CT: {centerTarget} {centerTargetBone}");
#endif
            OffsetPosition = Vector3.zero;
            PreviousPosition = Studio.Studio.Instance.cameraCtrl.cameraData.pos;
            lastFrameInit = false;

            if (centerTarget == -1)
            {
                CenterTargetKey = -1;
                CenterTarget = null;
                CenterTargetText.text = "Cntr: No center set.";
                return;
            }
            else
            {
                CenterTargetKey = centerTarget;
            }

            if (Studio.Studio.Instance.dicObjectCtrl.TryGetValue(centerTarget, out ObjectCtrlInfo oci))
            {
                if (centerTargetBone == null)
                {
                    CenterTarget = oci.guideObject.transformTarget;
                    CenterTargetText.text = $"Cntr: {oci.treeNodeObject.textName}";
                }
                else
                {
                    CenterTarget = FindDescendant(oci.guideObject.transformTarget, centerTargetBone);
                    CenterTargetText.text = $"Cntr: {oci.treeNodeObject.textName}\n{centerTargetBone}";
                }               
            }
        }

        private bool lastFrameInit = false;
        private Vector3 lastFramePosition;

        private void Update()
        {
            if (KKAPI.Studio.StudioAPI.InsideStudio)
            {
                if (Studio.Studio.Instance != null && Studio.Studio.Instance.cameraCtrl != null)
                {
                    if (initialCam == null)
                        initialCam = Studio.Studio.Instance.sceneInfo.cameraData[0];

                    if (CenterTarget != null)
                    {
                        LockOnButton.gameObject.SetActive(true);
                        LockOnButton.interactable = true;
                        ToggleCenterButton.gameObject.SetActive(true);
                        ClearCenterButton.interactable = true;                        
                        if (LockOnEnabled)
                        {
                            PositionLockButton.gameObject.SetActive(true);
                            if (PositionLocked)
                            {
                                lastFrameInit = false;
                            }
                            else
                            {
                                if (lastFrameInit)
                                    OffsetPosition += Studio.Studio.Instance.cameraCtrl.cameraData.pos - lastFramePosition;
                                else
                                    lastFrameInit = true;
                            }

                            Studio.Studio.Instance.cameraCtrl.cameraData.pos = CenterTarget.position + OffsetPosition;
                            lastFramePosition = Studio.Studio.Instance.cameraCtrl.cameraData.pos;
                        }
                        else
                        {
                            PositionLockButton.gameObject.SetActive(false);
                        }
                    }
                    else
                    {
                        PositionLockButton.gameObject.SetActive(false);
                        LockOnButton.gameObject.SetActive(true);
                        LockOnButton.interactable = false;
                        ToggleCenterButton.gameObject.SetActive(false);
                    }

                    if (ui.activeSelf)
                    { 
                        if (!PositionX.isFocused)
                            PositionX.text = string.Format("{0:F2}", LockOnEnabled ? OffsetPosition.x : Studio.Studio.Instance.cameraCtrl.cameraData.pos.x);
                        if (!PositionY.isFocused)
                            PositionY.text = string.Format("{0:F2}", LockOnEnabled ? OffsetPosition.y : Studio.Studio.Instance.cameraCtrl.cameraData.pos.y);
                        if (!PositionZ.isFocused)
                            PositionZ.text = string.Format("{0:F2}", LockOnEnabled ? OffsetPosition.z : Studio.Studio.Instance.cameraCtrl.cameraData.pos.z);

                        if (!RotationX.isFocused)
                            RotationX.text = string.Format("{0:F1}", Studio.Studio.Instance.cameraCtrl.cameraData.rotate.x);
                        if (!RotationY.isFocused)
                            RotationY.text = string.Format("{0:F1}", Studio.Studio.Instance.cameraCtrl.cameraData.rotate.y);
                        if (!RotationZ.isFocused)
                            RotationZ.text = string.Format("{0:F1}", Studio.Studio.Instance.cameraCtrl.cameraData.rotate.z);

                        if (!Distance.isFocused)
                            Distance.text = string.Format("{0:F2}", -1 * Studio.Studio.Instance.cameraCtrl.cameraData.distance.z);
                    }
                    for (int i = 0; i < MoarCamz.Count; i++)
                    {
                        if (i < 10 && !CamIsSet(Studio.Studio.Instance.sceneInfo.cameraData[i], MoarCamz[i]))
                            MoarCamz[i].CameraImage.color = Color.gray;
                        else if (i < 10 && (CompareCamData(Studio.Studio.Instance.sceneInfo.cameraData[i], Studio.Studio.Instance.cameraCtrl.cameraData, MoarCamz.Find((mc) => mc.SlotNumber == i + 1))))
                            MoarCamz[i].CameraImage.color = Color.green;
                        else if (i >= 10 && !CamIsSet(MoarCamz[i]))
                            MoarCamz[i].CameraImage.color = Color.gray;
                        else if (i >= 10 && (CompareCamData(MoarCamz[i], Studio.Studio.Instance.cameraCtrl.cameraData)))
                            MoarCamz[i].CameraImage.color = Color.green;
                        else if (lastSelectedCamera == i)
                            MoarCamz[i].CameraImage.color = Color.blue;
                        else
                        {
                            MoarCamz[i].CameraImage.color = Color.white;
                        }                                               
                    }                    
                }

                if (NextCameraButton.Value.IsDown())
                {
                    int originalValue = lastSelectedCamera;
                    Log.LogInfo($"Original Value {originalValue}");
                    bool found = false;
                    while (!found)
                    {
                        lastSelectedCamera++;
                        if (lastSelectedCamera >= MoarCamz.Count)
                            lastSelectedCamera = 0;
                        if (lastSelectedCamera < 10 && CamIsSet(Studio.Studio.Instance.sceneInfo.cameraData[lastSelectedCamera], MoarCamz.Find((mc) => mc.SlotNumber == lastSelectedCamera + 1)))
                            found = true;
                        else if (lastSelectedCamera >= 10 && CamIsSet(MoarCamz.Find((mc) => mc.SlotNumber == lastSelectedCamera + 1)))
                            found = true;
                        else if (originalValue == -1)
                            originalValue = 0;
                        else if (originalValue == lastSelectedCamera)
                            break;
                        Log.LogInfo($"Found: {found} LC: {lastSelectedCamera}");
                    }
                    Log.LogInfo($"Using: {found} LC: {lastSelectedCamera}");
                    if (lastSelectedCamera < 10)
                        Studio.Studio.Instance.cameraCtrl.Import(Studio.Studio.Instance.sceneInfo.cameraData[lastSelectedCamera]);
                    else
                        MoarCamz.Find((mc) => mc.SlotNumber == lastSelectedCamera + 1).Load(true);
                }
                else if (PrevCameraButton.Value.IsDown())
                {
                    int originalValue = lastSelectedCamera;
                    Log.LogInfo($"Original Value {originalValue}");
                    bool found = false;
                    while (!found)
                    {
                        lastSelectedCamera--;
                        if (lastSelectedCamera < 0)
                            lastSelectedCamera = MoarCamz.Count - 1;
                        if (lastSelectedCamera < 10 && CamIsSet(Studio.Studio.Instance.sceneInfo.cameraData[lastSelectedCamera], MoarCamz.Find((mc) => mc.SlotNumber == lastSelectedCamera + 1)))
                            found = true;
                        else if (lastSelectedCamera >= 10 && CamIsSet(MoarCamz.Find((mc) => mc.SlotNumber == lastSelectedCamera + 1)))
                            found = true;
                        else if (originalValue == -1)
                            originalValue = MoarCamz.Count - 1;
                        else if (originalValue == lastSelectedCamera)
                            break;
                        Log.LogInfo($"Found: {found} LC: {lastSelectedCamera}");

                    }

                    Log.LogInfo($"Using: {found} LC: {lastSelectedCamera}");
                    if (lastSelectedCamera < 10)
                        Studio.Studio.Instance.cameraCtrl.Import(Studio.Studio.Instance.sceneInfo.cameraData[lastSelectedCamera]);
                    else
                        MoarCamz.Find((mc) => mc.SlotNumber == lastSelectedCamera + 1).Load(true);
                }
            }
        }        

        private void LateUpdate()
        {

        }

        private void SetPositionX(float x)
        {
            if (LockOnEnabled)
                SetPosition(new Vector3(x, OffsetPosition.y, OffsetPosition.z));
            else
                SetPosition(new Vector3(x, Studio.Studio.Instance.cameraCtrl.cameraData.pos.y, Studio.Studio.Instance.cameraCtrl.cameraData.pos.z));
        }

        private void AddPositionX(float x)
        {
            Quaternion rotation = Studio.Studio.Instance.cameraCtrl.cameraData.rotation;
            Vector3 compensatedDirection = -1 * (rotation * new Vector3(x, 0, 0));
            if (LockOnEnabled && !PositionLocked)
                offsetPosition = OffsetPosition + compensatedDirection;
            else if (!LockOnEnabled)
                SetPosition(Studio.Studio.Instance.cameraCtrl.cameraData.pos + compensatedDirection);
        }

        private void SetPositionY(float y)
        {
            if (LockOnEnabled)
                SetPosition(new Vector3(OffsetPosition.x, y, OffsetPosition.z));
            else
                SetPosition(new Vector3(Studio.Studio.Instance.cameraCtrl.cameraData.pos.x, y, Studio.Studio.Instance.cameraCtrl.cameraData.pos.z));
        }
        private void AddPositionY(float y)
        {
            Quaternion rotation = Studio.Studio.Instance.cameraCtrl.cameraData.rotation;
            Vector3 compensatedDirection = rotation * new Vector3(0, y, 0);
            if (LockOnEnabled && !PositionLocked)
                offsetPosition = OffsetPosition + compensatedDirection;
            else if (!LockOnEnabled)
                SetPosition(Studio.Studio.Instance.cameraCtrl.cameraData.pos + compensatedDirection);
        }

        private void SetPositionZ(float z)
        {
            if (LockOnEnabled)
                SetPosition(new Vector3(OffsetPosition.x, OffsetPosition.y, z));
            else
                SetPosition(new Vector3(Studio.Studio.Instance.cameraCtrl.cameraData.pos.x, Studio.Studio.Instance.cameraCtrl.cameraData.pos.y, z));
        }
        private void AddPositionZ(float z)
        {
            Quaternion rotation = Studio.Studio.Instance.cameraCtrl.cameraData.rotation;
            Vector3 compensatedDirection = -1 * (rotation * new Vector3(0, 0, z));
            if (LockOnEnabled && !PositionLocked)
                offsetPosition = OffsetPosition + compensatedDirection;
            else if (!LockOnEnabled)
                SetPosition(Studio.Studio.Instance.cameraCtrl.cameraData.pos + compensatedDirection);
        }

        private void SetRotationX(float x)
        {
            SetRotation(new Vector3(x, Studio.Studio.Instance.cameraCtrl.cameraData.rotate.y, Studio.Studio.Instance.cameraCtrl.cameraData.rotate.z));
        }

        private void AddRotationX(float x)
        {
            SetRotationX(Studio.Studio.Instance.cameraCtrl.cameraData.rotate.x + x);
        }

        private void SetRotationY(float y)
        {
            SetRotation(new Vector3(Studio.Studio.Instance.cameraCtrl.cameraData.rotate.x, y, Studio.Studio.Instance.cameraCtrl.cameraData.rotate.z));
        }
        private void AddRotationY(float y)
        {
            SetRotationY(Studio.Studio.Instance.cameraCtrl.cameraData.rotate.y + y);
        }

        private void SetRotationZ(float z)
        {
            SetRotation(new Vector3(Studio.Studio.Instance.cameraCtrl.cameraData.rotate.x, Studio.Studio.Instance.cameraCtrl.cameraData.rotate.y, z));
        }

        private void AddRotationZ(float z)
        {
            SetRotationZ(Studio.Studio.Instance.cameraCtrl.cameraData.rotate.z + z);
        }

        private void SetPosition(Vector3 position)
        {
            if (!LockOnEnabled)
            {
                Studio.Studio.Instance.cameraCtrl.cameraData.pos = position;
#if DEBUG
                Log.LogInfo($"Cam Pos: {Studio.Studio.Instance.cameraCtrl.cameraData.pos} Rot: {Studio.Studio.Instance.cameraCtrl.cameraData.rotate} Dis: {Studio.Studio.Instance.cameraCtrl.cameraData.distance}");
#endif
            }
            else if (!PositionLocked)
            {
                offsetPosition = position;
#if DEBUG
                Log.LogInfo($"Cam Pos: {offsetPosition} Rot: {Studio.Studio.Instance.cameraCtrl.cameraData.rotate} Dis: {Studio.Studio.Instance.cameraCtrl.cameraData.distance}");
#endif
            }
        }

        private void SetRotation(Vector3 rotation)
        {            
            Studio.Studio.Instance.cameraCtrl.cameraData.rotate = rotation;
#if DEBUG
            Log.LogInfo($"Cam Pos: {Studio.Studio.Instance.cameraCtrl.cameraData.pos} Rot: {Studio.Studio.Instance.cameraCtrl.cameraData.rotate} Dis: {Studio.Studio.Instance.cameraCtrl.cameraData.distance}");
#endif
        }

        private void SetDistance(float distance)
        {
            Studio.Studio.Instance.cameraCtrl.cameraData.distance.z = distance * -1f;
#if DEBUG
            Log.LogInfo($"Cam Pos: {Studio.Studio.Instance.cameraCtrl.cameraData.pos} Rot: {Studio.Studio.Instance.cameraCtrl.cameraData.rotate} Dis: {Studio.Studio.Instance.cameraCtrl.cameraData.distance}");
#endif
        }
        private void AddDistance(float d)
        {
            SetDistance((Studio.Studio.Instance.cameraCtrl.cameraData.distance.z * -1f) + d);
        }

        private bool CompareCamData(CameraData first, CameraData second)
        {
            if (first == null && second == null)
                return false;
            else if (first != null && second == null)
                return false;
            else if (first == null && second != null)
                return false;
            else
            {
                if (first.pos.Equals(second.pos) && first.rotate.Equals(second.rotate) && first.distance.Equals(second.distance))
                    return true;
                else
                    return false;
            }
        }

        private bool CompareCamData(CameraData first, CameraData second, MoarCamzData moarCamzData)
        {
            if (moarCamzData == null)
                return false;
            else if (first == null && second == null)
                return false;
            else if (first != null && second == null)
                return false;
            else if (first == null && second != null)
                return false;
            else
            {
                if (LockOnEnabled)
                {
                    if (moarCamzData.OffsetPosition.Equals(OffsetPosition) && first.rotate.Equals(second.rotate) && first.distance.Equals(second.distance) && moarCamzData.CenterTarget == CenterTargetKey && CompareBoneNames(moarCamzData.CenterTargetBone, CenterTarget))
                        return true;
                    else
                        return false;
                }
                else
                {
                    if (first.pos.Equals(second.pos) && first.rotate.Equals(second.rotate) && first.distance.Equals(second.distance) && moarCamzData.CenterTarget == CenterTargetKey && CompareBoneNames(moarCamzData.CenterTargetBone, CenterTarget))
                        return true;
                    else
                        return false;
                }
            }
        }

        private bool CompareBoneNames(string boneName, Transform target)
        {
            if (boneName == null && target == null)
                return true;
            else
            {
                if (boneName == null)
                    return false;
                else if (target == null)
                    return false;
                else
                    return boneName == target.name;
            }
        }

        private bool CompareCamData(MoarCamzData first, CameraData second)
        {
            if (first == null && second == null)
                return false;
            else if (first != null && second == null)
                return false;
            else if (first == null && second != null)
                return false;
            else if (first == null)
                return false;
            else
            {
                if (LockOnEnabled)
                {
                    if (first.OffsetPosition.Equals(OffsetPosition) && first.Rotation.Equals(second.rotate) && first.Distance.Equals(second.distance) && first.CenterTarget == CenterTargetKey && CompareBoneNames(first.CenterTargetBone, CenterTarget))
                        return true;
                    else
                        return false;
                }
                else
                {
                    if (first.Position.Equals(second.pos) && first.Rotation.Equals(second.rotate) && first.Distance.Equals(second.distance) && first.CenterTarget == CenterTargetKey && CompareBoneNames(first.CenterTargetBone, CenterTarget))
                        return true;
                    else
                        return false;
                }
            }
        }

        private bool CompareCamData(MoarCamzData first, CameraData second, bool checkExtended)
        {
            if (first == null && second == null)
                return false;
            else if (first != null && second == null)
                return false;
            else if (first == null && second != null)
                return false;
            else
            {
                if (!checkExtended)
                {
                    if (first.Position.Equals(second.pos) && first.Rotation.Equals(second.rotate) && first.Distance.Equals(second.distance))
                        return true;
                    else
                        return false;
                }
                else
                {
                    if (first.Position.Equals(second.pos) && first.Rotation.Equals(second.rotate) && first.Distance.Equals(second.distance) && first.CenterTarget == CenterTargetKey && CompareBoneNames(first.CenterTargetBone, CenterTarget))
                        return true;
                    else
                        return false;
                }
            }
        }

        public void AddCameraSlot(int slotNumber, MoarCamzData data)
        {
            RectTransform newCamSlotTransform = (RectTransform)GameObject.Instantiate(cameraSlotFab, cameraScroll.content).transform;
            newCamSlotTransform.gameObject.name = (slotNumber - 1).ToString("00");
            newCamSlotTransform.localScale = cameraSlotFab.transform.localScale;
            newCamSlotTransform.localPosition = new Vector3(cameraSlotFab.transform.localPosition.x + (40 * (slotNumber - 1)), cameraSlotFab.transform.localPosition.y, cameraSlotFab.transform.localPosition.z);
            data.CameraRect = newCamSlotTransform;
            data.CameraImage = newCamSlotTransform.transform.Find("Button Load").GetComponent<Image>();
            Button saveButton = newCamSlotTransform.Find("Button Save").GetComponent<Button>();
            saveButton.onClick = new Button.ButtonClickedEvent();
            saveButton.onClick.AddListener(() => { lastSelectedCamera = slotNumber - 1; data.Save(); });
            Button loadButton = newCamSlotTransform.Find("Button Load").GetComponent<Button>();
            loadButton.onClick = new Button.ButtonClickedEvent();
            loadButton.onClick.AddListener(() => { lastSelectedCamera = slotNumber - 1; data.Load(true); });
            loadButton.GetComponent<Image>().sprite = blankCameraPrefab.GetComponent<Image>().sprite;
            Transform textGO = GameObject.Instantiate(blankCameraPrefab.transform.Find("CamNumText"), loadButton.transform);
            textGO.GetComponent<Text>().text = slotNumber.ToString();
            ResizeCameraScroll();
            DelCamButton.gameObject.SetActive(true);
        }

        public void DelCameraSlot(MoarCamzData data)
        {
            if (data != null)
            {
                GameObject.Destroy(data.CameraRect.gameObject);
                MoarCamz.Remove(data);

                if (MoarCamz.Count <= 10)
                    DelCamButton.gameObject.SetActive(false);

                ResizeCameraScroll();
            }
        }

        public void DelCameraSlot(int slotNumber)
        {
            MoarCamzData data = MoarCamz.Find(mc => mc.SlotNumber == slotNumber);
            DelCameraSlot(data);
        }

        private void ResizeCameraScroll()
        {
#if KKS
            cameraScroll.content.sizeDelta = new Vector2(450 + (40 * (MoarCamz.Count - 10)), cameraScroll.content.sizeDelta.y);            
#else
            cameraScroll.content.sizeDelta = new Vector2(432 + (40 * (MoarCamz.Count - 10)), cameraScroll.content.sizeDelta.y);
#endif
        }

        private static void OnClickLoadCameraPostfix(int _no)
        {
            Instance.lastSelectedCamera = _no;
            MoarCamzPlugin.Instance.MoarCamz[_no].Load(false);
        }
        private static void OnClickSaveCameraPostfix(int _no)
        {
            Instance.lastSelectedCamera = _no;
            MoarCamzPlugin.Instance.MoarCamz[_no].Save();            
        }

        private static void CameraControlInternalUpdateCameraStatePostfix(Studio.CameraControl __instance, Renderer ___m_TargetRender)
        {
            if (dragging && __instance.isOutsideTargetTex && __instance.isConfigTargetTex)
            {
                ___m_TargetRender.enabled = true;
            }
            else
            {
                ___m_TargetRender.enabled = __instance.isControlNow && __instance.isOutsideTargetTex && __instance.isConfigTargetTex;
            }
            dragging = false;
        }

        private Transform FindDescendant(Transform start, string name)
        {
            if (start == null)
            {
                return null;
            }

            if (start.name.Equals(name))
            {
                return start;
            }
            foreach (Transform t in start)
            {
                Transform res = FindDescendant(t, name);
                if (res != null)
                {
                    return res;
                }
            }
            return null;
        }

        private bool CamIsSet(MoarCamzData data)
        {
            return !(CompareCamData(data, newCam) || CompareCamData(data, initialCam) || CompareCamData(data, resetCam)) || data.CenterTarget != -1;
        }

        private bool CamIsSet(CameraData camData, MoarCamzData moarCamzData)
        {

            return !(CompareCamData(camData, newCam) || CompareCamData(camData, initialCam) || CompareCamData(camData, resetCam)) || moarCamzData.CenterTarget != -1;
        }

        public void SetResetCam()
        {
            resetCam = Studio.Studio.Instance.sceneInfo.cameraData[0];
        }        

        // private vars
        private CameraData initialCam;
        private CameraData resetCam;
        private CameraData newCam = new CameraData();
        private int lastSelectedCamera = -1;
        private bool positionSelected = true;
        private static bool dragging;

        // UI References
        private Button AddCamButton;
        private Button DelCamButton;
        private Button ToggleCenterButton;
        private Button ToggleUIButton;

        private Button PositionButton;
        private Button RotationButton;
        private Button DistanceButton;

        private TMP_InputField PositionX, PositionY, PositionZ;
        private TMP_InputField RotationX, RotationY, RotationZ;
        private TMP_InputField Distance;

        private GameObject FastNav;
        private GameObject SlowNav;
        private GameObject ScrollNav;
        private GameObject ui;
        private GameObject additionalButtons;

        private Image XZScroll, XScroll, YScroll, ZScroll, DScroll;
        private TextMeshProUGUI ScrollY;
        private Button ScrollXZButton, ScrollXYButton;
        private Button LockOnButton, SetCenterButton, ClearCenterButton, PositionLockButton;
        private TextMeshProUGUI CenterTargetText;
    }
}