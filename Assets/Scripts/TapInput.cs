using UnityEngine;

namespace Runner
{
    /// <summary>
    /// TapInput — Lit le tap (clic / doigt / barre espace) QUELLE QUE SOIT la
    /// configuration "Active Input Handling" du projet (Legacy, Input System, ou Both).
    ///
    /// Pourquoi ce helper existe :
    ///  - Avec le nouveau Input System seul, les anciens appels (Input.GetMouseButtonDown)
    ///    lancent une InvalidOperationException à l'exécution → le jeu "ne répond pas".
    ///  - Ici, les DEUX chemins sont tentés (définis à la compilation, protégés à l'exécution) :
    ///    le tap fonctionne dans tous les cas, sans redémarrer Unity.
    ///
    /// Compatible Unity 2022+ / Unity 6.
    /// </summary>
    public static class TapInput
    {
        /// <summary>Vrai le frame où l'utilisateur vient de cliquer / toucher / presser Espace.</summary>
        public static bool WasPressed()
        {
            bool tapped = false;

#if ENABLE_LEGACY_INPUT_MANAGER
            // Ancien Input : protégé — peut être désactivé à l'exécution si le projet
            // est réglé sur "Input System Package" seulement.
            try
            {
                tapped = Input.GetMouseButtonDown(0)
                      || Input.GetKeyDown(KeyCode.Space)
                      || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
            }
            catch (System.InvalidOperationException)
            {
                // Legacy désactivé : le nouveau Input System prend le relais ci-dessous.
            }
#endif

#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) tapped = true;

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) tapped = true;

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) tapped = true;
#endif

            return tapped;
        }
    }

    /// <summary>
    /// BlobInput — Entrées du gameplay "collecteur de gelatine".
    ///  - Déplacement latéral : GLISSE (drag) sur écran / souris, ou A-D / flèches.
    ///  - Saut : TAP COURT (sans glisser), ou Espace / ↑ / W.
    /// Double-système comme TapInput : fonctionne quel que soit le réglage d'input.
    /// </summary>
    public static class BlobInput
    {
        /// <summary>Axe latéral clavier : -1 (gauche) à +1 (droite), 0 si rien.</summary>
        public static float KeyboardAxis()
        {
            float axis = 0f;

#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) axis -= 1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) axis += 1f;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (axis == 0f)
            {
                try
                {
                    if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) axis -= 1f;
                    if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) axis += 1f;
                }
                catch (System.InvalidOperationException)
                {
                    // Legacy désactivé : le clavier a déjà été lu via l'Input System.
                }
            }
#endif

            return axis;
        }

        /// <summary>Vrai le frame où une touche de saut vient d'être pressée.</summary>
        public static bool JumpKey()
        {
            bool jump = false;

#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame
                    || kb.upArrowKey.wasPressedThisFrame
                    || kb.wKey.wasPressedThisFrame) jump = true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (!jump)
            {
                try
                {
                    jump = Input.GetKeyDown(KeyCode.Space)
                        || Input.GetKeyDown(KeyCode.UpArrow)
                        || Input.GetKeyDown(KeyCode.W);
                }
                catch (System.InvalidOperationException)
                {
                    // Legacy désactivé.
                }
            }
#endif

            return jump;
        }

        /// <summary>
        /// Lit le pointeur (souris OU doigt) : position + événements began/released.
        /// Le doigt a priorité sur la souris (mobile). On ne lit les deux systèmes
        /// qu'une seule fois par frame quand c'est possible (pas de double comptage).
        /// </summary>
        public static void ReadPointer(out bool down, out Vector2 pos, out bool began, out bool released)
        {
            down = false; pos = Vector2.zero; began = false; released = false;

#if ENABLE_INPUT_SYSTEM
            // --- Doigt (prioritaire sur la souris, comme sur mobile) ---
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null)
            {
                var t = touch.primaryTouch;
                bool pressed = t.press.isPressed;
                if (pressed || t.press.wasReleasedThisFrame)
                {
                    down = pressed;
                    pos = t.position.ReadValue();   // "position" = coordonnées écran du doigt
                    began = t.press.wasPressedThisFrame;
                    released = t.press.wasReleasedThisFrame;
                    return;
                }
            }

            // --- Souris (si pas de doigt actif) ---
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                down = mouse.leftButton.isPressed;
                pos = mouse.position.ReadValue();
                began = mouse.leftButton.wasPressedThisFrame;
                released = mouse.leftButton.wasReleasedThisFrame;
            }
            return;
#endif

#if !ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
            // Fallback Legacy (utilisé si l'Input System n'est pas installé du tout).
            try
            {
                if (Input.touchCount > 0)
                {
                    var t = Input.GetTouch(0);
                    down = t.phase == TouchPhase.Began || t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
                    pos = t.position;
                    began = t.phase == TouchPhase.Began;
                    released = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                }
                else
                {
                    down = Input.GetMouseButton(0);
                    pos = Input.mousePosition;
                    began = Input.GetMouseButtonDown(0);
                    released = Input.GetMouseButtonUp(0);
                }
            }
            catch (System.InvalidOperationException)
            {
                // Legacy désactivé : rien à lire de plus.
            }
#endif
        }
    }
}