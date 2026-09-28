using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// Keyboard shortcuts. Both input backends are covered so the keys work whether the project is set
    /// to the legacy manager, the new Input System, or both.
    /// </summary>
    public static class Hotkeys
    {
        /// <summary>M opens the dungeon map.</summary>
        public static bool MapPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.M)) return true;
#endif
            return false;
        }

        /// <summary>I opens the character menu (skills, deck, items).</summary>
        public static bool CharacterPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.iKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.I)) return true;
#endif
            return false;
        }

        /// <summary>Escape backs out of an overlay without leaving the screen behind it.</summary>
        public static bool CancelPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) return true;
#endif
            return false;
        }
    }
}
