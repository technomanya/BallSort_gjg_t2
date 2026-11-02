using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace BallSortGame
{
    /// <summary>
    /// The one place the game reads the pointer (finger or mouse). The editor and dev builds go through
    /// DevPointerInput, which marks and logs each tap and fixes coordinates on emulators like BlueStacks.
    /// </summary>
    public static class PointerInput
    {
        /// <summary>Editor or development build.</summary>
        public static bool IsDev => Application.isEditor || Debug.isDebugBuild;

        /// <summary>True on the frame the finger touches down or the mouse is pressed.</summary>
        public static bool Down => Input.GetMouseButtonDown(0);

        /// <summary>Screen pixels, origin bottom left.</summary>
        public static Vector2 Position => IsDev ? DevPointerInput.Correct(Input.mousePosition) : (Vector2)Input.mousePosition;

        /// <summary>
        /// True when the current tap landed on UI. With a touch we ask about that finger, because on Android the
        /// mouse pointer only refreshes on frames without a touch and still reports the previous tap's button.
        /// </summary>
        public static bool IsOverUi()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                    if (es.IsPointerOverGameObject(Input.GetTouch(i).fingerId)) return true;
                return false;
            }
            return es.IsPointerOverGameObject();
        }
    }

    /// <summary>
    /// Dev-build pointer path, never attached in release. Hooks in as the StandaloneInputModule's inputOverride so
    /// uGUI and board taps see the same corrected position. Draws a cross where each tap landed and, in a build,
    /// logs the screen numbers.
    /// If the display size differs from Screen and a raw tap ever lands off screen, touches are in display pixels,
    /// so from then on positions are scaled into screen pixels.
    /// </summary>
    public class DevPointerInput : BaseInput
    {
        static DevPointerInput _instance;
        static bool _scaleLatched;
        static Vector2 _scale = Vector2.one;
        static Vector2 _lastRaw, _lastUsed;
        static float _lastDownTime = -10f;
        static Texture2D _dot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!PointerInput.IsDev) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Hook();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Hook();

        public static void Hook()
        {
            if (!PointerInput.IsDev) return;
            var module = Object.FindFirstObjectByType<StandaloneInputModule>();
            if (module == null || module.inputOverride is DevPointerInput) return;
            _instance = module.gameObject.GetComponent<DevPointerInput>();
            if (_instance == null) _instance = module.gameObject.AddComponent<DevPointerInput>();
            module.inputOverride = _instance;
        }

        /// <summary>Maps a raw pointer position into screen pixels.</summary>
        public static Vector2 Correct(Vector2 raw)
        {
            if (_instance == null) Hook();
            var d = Display.main;
            bool mismatch = d != null && (d.systemWidth != Screen.width || d.systemHeight != Screen.height);
            if (mismatch && !_scaleLatched && (raw.x > Screen.width + 1f || raw.y > Screen.height + 1f))
            {
                _scaleLatched = true;
                _scale = new Vector2(Screen.width / (float)d.systemWidth, Screen.height / (float)d.systemHeight);
                Debug.Log($"Pointer: raw position {raw} is outside the {Screen.width}x{Screen.height} screen while the display is {d.systemWidth}x{d.systemHeight}: touch positions are now scaled by {_scale}.");
            }
            Vector2 used = _scaleLatched ? Vector2.Scale(raw, _scale) : raw;
            if (Input.GetMouseButtonDown(0) && Time.unscaledTime != _lastDownTime)
            {
                _lastDownTime = Time.unscaledTime;
                _lastRaw = raw;
                _lastUsed = used;
                if (!Application.isEditor)
                {
                    var es = EventSystem.current;
                    string ui = es != null && es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "none";
                    string sys = d != null ? $"{d.systemWidth}x{d.systemHeight}" : "?";
                    string ren = d != null ? $"{d.renderingWidth}x{d.renderingHeight}" : "?";
                    Debug.Log($"Pointer: raw {raw} -> used {used} | Screen {Screen.width}x{Screen.height} | system {sys} | rendering {ren} | safe {Screen.safeArea} | {Screen.orientation} | ui {ui}");
                }
            }
            return used;
        }

        // BaseInput overrides: only the positions change.
        public override Vector2 mousePosition => Correct(Input.mousePosition);

        public override Touch GetTouch(int index)
        {
            var t = Input.GetTouch(index);
            if (_scaleLatched) t.position = Vector2.Scale(t.position, _scale);
            return t;
        }

        void OnGUI()
        {
            float age = Time.unscaledTime - _lastDownTime;
            if (age > 0.5f) return;
            if (_dot == null)
            {
                _dot = new Texture2D(1, 1);
                _dot.SetPixel(0, 0, Color.white);
                _dot.Apply();
            }
            float r = Mathf.Max(14f, Screen.height / 80f);
            var p = new Vector2(_lastUsed.x, Screen.height - _lastUsed.y);  // GUI space: origin top left
            var c = GUI.color;
            GUI.color = new Color(1f, 0.3f, 0.2f, 1f - age * 2f);
            GUI.DrawTexture(new Rect(p.x - r, p.y - 2f, 2f * r, 4f), _dot);
            GUI.DrawTexture(new Rect(p.x - 2f, p.y - r, 4f, 2f * r), _dot);
            GUI.color = c;
            if (!Application.isEditor)
            {
                var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 60) };
                style.normal.textColor = Color.white;
                GUI.Label(new Rect(10, 10, Screen.width - 20, style.fontSize * 3),
                    $"tap raw {_lastRaw} used {_lastUsed}\nscreen {Screen.width}x{Screen.height} display {Display.main.systemWidth}x{Display.main.systemHeight}", style);
            }
        }
    }
}
