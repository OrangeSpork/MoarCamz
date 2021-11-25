using MessagePack;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using static Studio.CameraControl;

namespace MoarCamz
{
    [Serializable]
    [MessagePackObject(true)]
    public class MoarCamzData
    {
        public int SlotNumber { get; set; }

        public Vector3 Position { get; set; }

        public Vector3 Rotation { get; set; }

        public Vector3 Distance { get; set; }

        public float FOV { get; set; } = 23;

        public Vector3 OffsetPosition { get; set; }

        public Vector3 PreviousPosition { get; set; }

        public int CenterTarget { get; set; } = -1;
        public string CenterTargetBone { get; set; }

        public bool CenterTargetEngaged { get; set; }

        public bool PositionLocked { get; set; }

        [IgnoreMember]
        public RectTransform CameraRect { get; set; }
        [IgnoreMember]
        public Image CameraImage { get; set; }

        public void Clear()
        {
            Position = Vector3.zero;
            Rotation = Vector3.zero;
            Distance = Vector3.zero;
            FOV = 23;            
        }

        public void ClearExtended()
        {
            OffsetPosition = Vector3.zero;
            PreviousPosition = Vector3.zero;
            CenterTarget = -1;
            CenterTargetBone = null;
            CenterTargetEngaged = false;
            PositionLocked = false;
        }

        public void Load(bool extended)
        {
            MoarCamzPlugin.Instance.SetCenterTarget(CenterTarget, CenterTargetBone);
            MoarCamzPlugin.Instance.OffsetPosition = OffsetPosition;
            MoarCamzPlugin.Instance.LockOnEnabled = CenterTargetEngaged;
            MoarCamzPlugin.Instance.PreviousPosition = PreviousPosition;
            MoarCamzPlugin.Instance.PositionLocked = PositionLocked;

#if DEBUG
            MoarCamzPlugin.Instance.Log.LogInfo($"Loading {CenterTarget} {CenterTargetBone}");
#endif            

            if (extended)
            {
                CameraData cameraData = new CameraData();
                cameraData.pos = Position;
                cameraData.rotate = Rotation;
                cameraData.distance = Distance;
                cameraData.parse = FOV;
                Studio.Studio.Instance.cameraCtrl.Import(cameraData);
            }
        }

        public void Save()
        {
            Position = Studio.Studio.Instance.cameraCtrl.cameraData.pos;
            Rotation = Studio.Studio.Instance.cameraCtrl.cameraData.rotate;
            Distance = Studio.Studio.Instance.cameraCtrl.cameraData.distance;
            FOV = Studio.Studio.Instance.cameraCtrl.cameraData.parse;
            OffsetPosition = MoarCamzPlugin.Instance.OffsetPosition;
            PreviousPosition = MoarCamzPlugin.Instance.PreviousPosition;
            PositionLocked = MoarCamzPlugin.Instance.PositionLocked;
            CenterTargetEngaged = MoarCamzPlugin.Instance.LockOnEnabled;
            CenterTarget = MoarCamzPlugin.Instance.CenterTargetKey;
            if (CenterTarget != -1)
                CenterTargetBone = MoarCamzPlugin.Instance.CenterTarget?.name;
            else
                CenterTargetBone = null;

#if DEBUG
            MoarCamzPlugin.Instance.Log.LogInfo($"Saving {CenterTarget} {CenterTargetBone}");
#endif            

        }

        internal void Copy(CameraData camData)
        {
            Position = camData.pos;
            Rotation = camData.rotate;
            Distance = camData.distance;
            FOV = camData.parse;
        }

        internal void Copy(MoarCamzData camzData)
        {
            CenterTarget = camzData.CenterTarget;
            CenterTargetBone = camzData.CenterTargetBone;
            OffsetPosition = camzData.OffsetPosition;
            CenterTargetEngaged = camzData.CenterTargetEngaged;
            PreviousPosition = camzData.PreviousPosition;
            PositionLocked = camzData.PositionLocked;
        }
    }
}
