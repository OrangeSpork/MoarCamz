using BepInEx.Logging;
using ExtensibleSaveFormat;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using MessagePack;
using Studio;
using System;
using System.Collections.Generic;
using System.Text;

namespace MoarCamz
{
    class MoarCamzStudioController : SceneCustomFunctionController
    {
        private ManualLogSource Log => MoarCamzPlugin.Instance.Log;

        protected override void OnSceneLoad(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            Log.LogInfo($"Scene Loaded: {operation}");
            if (operation != SceneOperationKind.Import)
            {
                // wipe cams down to vanilla
                List<MoarCamzData> toDelete = MoarCamzPlugin.Instance.MoarCamz.FindAll((mc) => mc.SlotNumber > 10);
                foreach (MoarCamzData data in toDelete)
                {
                    MoarCamzPlugin.Instance.DelCameraSlot(data);
                }
                MoarCamzPlugin.Instance.MoarCamz.RemoveAll((mc) => mc.SlotNumber > 10);

                if (operation == SceneOperationKind.Load)
                {
                    // load new cams
                    PluginData pluginData = GetExtendedData();
                    if (pluginData != null && pluginData.data != null)
                    {
                        if (pluginData.data.TryGetValue("MoarCamz", out object moarCamzBinary))
                        {
                            List<MoarCamzData> camzDataList = (List<MoarCamzData>)MessagePackSerializer.Deserialize<List<MoarCamzData>>((byte[])moarCamzBinary);
                            foreach (MoarCamzData camzData in camzDataList)
                            {
                                MoarCamzData foundData = MoarCamzPlugin.Instance.MoarCamz.Find((mc) => mc.SlotNumber == camzData.SlotNumber);
                                if (foundData != null)
                                {
                                    foundData.Copy(camzData);
                                }
                                else
                                {
                                    MoarCamzPlugin.Instance.MoarCamz.Add(camzData);
                                    MoarCamzPlugin.Instance.AddCameraSlot(camzData.SlotNumber, camzData);
                                }
                            }
                        }
                    }
                }
                else
                {
                    MoarCamzPlugin.Instance.SetResetCam();
                }

                MoarCamzPlugin.Instance.LastSelectedCamera = -1;
            }
        }

        protected override void OnSceneSave()
        {
            PluginData pluginData = new PluginData();
            pluginData.data = new Dictionary<string, object>();
            pluginData.data["MoarCamz"] = MessagePackSerializer.Serialize<List<MoarCamzData>>(MoarCamzPlugin.Instance.MoarCamz);
            SetExtendedData(pluginData);
        }
    }
}
