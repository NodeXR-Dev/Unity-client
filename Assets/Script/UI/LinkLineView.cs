using UnityEngine;

namespace NodeXR.UI
{
    [RequireComponent(typeof(LineRenderer))]
    public class LinkLineView : MonoBehaviour
    {
        public Transform a;
        public Transform b;

        private LineRenderer _lr;

        private void Awake()
        {
            _lr = GetComponent<LineRenderer>();
            _lr.positionCount = 2;
            _lr.useWorldSpace = true;
        }

        private void LateUpdate()
        {
            if (!_lr || !a || !b) return;
            _lr.SetPosition(0, a.position);
            _lr.SetPosition(1, b.position);
        }
    }
}
