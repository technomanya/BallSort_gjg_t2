using UnityEngine;

namespace BallSortGame.Gameplay
{
    /// <summary>Scales a background sprite so it always covers the orthographic camera view (aspect-fill).</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [ExecuteAlways]
    public class BackgroundFitter : MonoBehaviour
    {
        public Camera cam;
        float _lastSize, _lastAspect;

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            var sr = GetComponent<SpriteRenderer>();
            if (sr.sprite == null) return;
            if (Mathf.Approximately(_lastSize, cam.orthographicSize) && Mathf.Approximately(_lastAspect, cam.aspect) && Application.isPlaying) return;
            _lastSize = cam.orthographicSize; _lastAspect = cam.aspect;

            float h = cam.orthographicSize * 2f;
            float w = h * cam.aspect;
            Vector2 s = sr.sprite.bounds.size;
            float k = Mathf.Max(w / s.x, h / s.y);
            transform.localScale = new Vector3(k, k, 1f);
            transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 10f);
        }
    }
}
