using UnityEngine;

namespace NodeXR
{
    public class EdgeRenderer : MonoBehaviour
    {
        public LineRenderer linePrefab;
        public Transform edgesRoot;

        private readonly System.Collections.Generic.List<LineRenderer> _lines = new();

        public void Clear()
        {
            foreach (var l in _lines)
                if (l) Destroy(l.gameObject);
            _lines.Clear();
        }

        public void DrawLine(Transform from, Transform to)
        {
            if (!linePrefab || !edgesRoot || !from || !to) return;

            var lr = Instantiate(linePrefab, edgesRoot);
            lr.positionCount = 2;
            lr.SetPosition(0, from.position);
            lr.SetPosition(1, to.position);
            _lines.Add(lr);
        }

        public void UpdateAll()
        {
            // v1: 간단하게 매 프레임 재계산하지 않고 필요 시 호출
            // (선이 움직일 일이 거의 없기 때문)
        }
    }
}
