using System.Collections;
using UnityEngine;

namespace BallSortGame.Gameplay
{
    /// <summary>
    /// Plays between the last move and the win screen: the board dims, the lettering pops up and fireworks burst.
    /// It is world-space so the HUD bars stay bright over it. Made by BallSort > Build Win Splash.
    /// </summary>
    public class WinSplash : MonoBehaviour
    {
        public GameManager game;
        public Camera cam;

        [Header("Parts (BallSort > Build Win Splash)")]
        [Tooltip("Full-view tint over the board.")]
        public SpriteRenderer dim;
        [Tooltip("The game's lettering (UISkin.logo).")]
        public SpriteRenderer logo;
        [Tooltip("One particle system per burst, played in order.")]
        public ParticleSystem[] fireworks = new ParticleSystem[0];

        [Header("Timing (seconds)")]
        public float FadeIn = 0.15f;
        [Tooltip("How long the lettering stays before the win screen.")]
        public float Hold = 0.5f;
        public float FadeOut = 0.2f;
        [Tooltip("Delay between one firework and the next.")]
        public float Stagger = 0.12f;

        [Header("Layout (fractions of the camera view)")]
        public Color DimColor = new Color(0.05f, 0.03f, 0.14f, 1f);
        [Range(0f, 1f)] public float DimAlpha = 0.85f;
        [Tooltip("Lettering width as a fraction of view width.")]
        public float LogoWidth = 0.72f;
        [Tooltip("Lettering centre, 0.5 = middle of the view.")]
        public Vector2 LogoCenter = new Vector2(0.5f, 0.5f);
        [Tooltip("Where each firework bursts (0,0 = bottom left).")]
        public Vector2[] BurstPoints = { new Vector2(0.28f, 0.66f), new Vector2(0.72f, 0.62f), new Vector2(0.5f, 0.3f) };
        [Tooltip("1 = prefab size at a 10 unit tall view.")]
        public float FireworkScale = 0.3f;
        [Tooltip("Dim order. Lettering +1, fireworks +2.")]
        public int SortingOrder = 100;

        public bool IsPlaying { get; private set; }

        /// <summary>The scene's splash, or null.</summary>
        public static WinSplash Current => FindFirstObjectByType<WinSplash>(FindObjectsInactive.Include);

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (dim != null) dim.gameObject.SetActive(false);
            if (logo != null) logo.gameObject.SetActive(false);
            foreach (var f in fireworks) if (f != null) f.gameObject.SetActive(false);
        }

        /// <summary>
        /// Hides the splash at once. GameManager runs Play in its own coroutine, so a restart must call this or the
        /// dim stays up and IsPlaying stays true.
        /// </summary>
        public void Cancel()
        {
            StopAllCoroutines();
            if (dim != null) dim.gameObject.SetActive(false);
            if (logo != null) logo.gameObject.SetActive(false);
            foreach (var f in fireworks)
                if (f != null) { f.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); f.gameObject.SetActive(false); }
            IsPlaying = false;
        }

        /// <summary>Finishes when the lettering has faded. Fireworks may still be falling.</summary>
        public IEnumerator Play()
        {
            if (IsPlaying) yield break;
            IsPlaying = true;
            if (cam == null) cam = Camera.main;
            Layout();

            if (dim != null) dim.gameObject.SetActive(true);
            if (logo != null) logo.gameObject.SetActive(true);
            StartCoroutine(Fireworks());

            Vector3 logoScale = logo != null ? logo.transform.localScale : Vector3.one;
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(0.01f, FadeIn));
                SetDim(DimAlpha * t);
                if (logo != null)
                {
                    // pop: 0.6 -> 1.1 -> 1
                    float k = t < 0.7f ? Mathf.Lerp(0.6f, 1.1f, t / 0.7f) : Mathf.Lerp(1.1f, 1f, (t - 0.7f) / 0.3f);
                    logo.transform.localScale = logoScale * k;
                    SetAlpha(logo, Mathf.Min(1f, t * 2f));
                }
                yield return null;
            }
            if (logo != null) logo.transform.localScale = logoScale;

            yield return new WaitForSeconds(Hold);

            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(0.01f, FadeOut));
                SetDim(DimAlpha * (1f - t));
                if (logo != null) SetAlpha(logo, 1f - t);
                yield return null;
            }
            if (dim != null) dim.gameObject.SetActive(false);
            if (logo != null) logo.gameObject.SetActive(false);
            foreach (var f in fireworks) if (f != null && f.isPlaying) f.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            IsPlaying = false;
        }

        IEnumerator Fireworks()
        {
            for (int i = 0; i < fireworks.Length; i++)
            {
                var f = fireworks[i];
                if (f != null)
                {
                    f.gameObject.SetActive(true);
                    f.Clear(true);
                    f.Play(true);
                }
                if (Stagger > 0f) yield return new WaitForSeconds(Stagger);
            }
        }

        // Re-fit to the camera every time because the board zoom changes per level.
        void Layout()
        {
            if (cam == null) return;
            float h = cam.orthographic ? cam.orthographicSize * 2f : 10f;
            float w = h * cam.aspect;
            var centre = cam.transform.position;
            transform.position = new Vector3(centre.x, centre.y, 0f);

            if (dim != null)
            {
                dim.transform.position = transform.position;
                dim.sortingOrder = SortingOrder;
                var size = dim.sprite != null ? dim.sprite.bounds.size : Vector3.one;
                dim.transform.localScale = Vector3.one * Mathf.Max(w / size.x, h / size.y) * 1.02f;
                SetDim(0f);
            }
            if (logo != null)
            {
                logo.sortingOrder = SortingOrder + 1;
                logo.transform.position = View(LogoCenter, w, h);
                float width = logo.sprite != null ? logo.sprite.bounds.size.x : 5f;
                logo.transform.localScale = Vector3.one * (w * LogoWidth / width);
                SetAlpha(logo, 0f);
            }
            for (int i = 0; i < fireworks.Length; i++)
            {
                var f = fireworks[i];
                if (f == null) continue;
                var at = i < BurstPoints.Length ? BurstPoints[i] : new Vector2(0.5f, 0.5f);
                f.transform.position = View(at, w, h);
                f.transform.localScale = Vector3.one * (h / 10f) * FireworkScale;
                foreach (var r in f.GetComponentsInChildren<ParticleSystemRenderer>(true)) r.sortingOrder = SortingOrder + 2 + Mathf.Max(0, r.sortingOrder % 10);
                foreach (var ps in f.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
            }
        }

        Vector3 View(Vector2 fraction, float w, float h) =>
            transform.position + new Vector3((fraction.x - 0.5f) * w, (fraction.y - 0.5f) * h, 0f);

        void SetDim(float alpha)
        {
            if (dim == null) return;
            var c = DimColor;
            c.a = alpha;
            dim.color = c;
        }

        static void SetAlpha(SpriteRenderer sr, float alpha)
        {
            var c = sr.color;
            c.a = alpha;
            sr.color = c;
        }

        [ContextMenu("Play")]
        void DebugPlay()
        {
            if (Application.isPlaying) StartCoroutine(Play());
        }
    }
}
