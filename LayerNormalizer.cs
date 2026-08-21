using System;
using MelonLoader;
using UnityEngine;

namespace SynthCamera2
{
    // v0.7.0 (19-08-2026): the game update moved the visible note/rail meshes
    // ("Note Model/Display Size Scaler/Standard" subtrees) and 83 objects of
    // the "[Score UI]" subtree onto layer 0 Default (layer dumps, 19-08-2026).
    // Layer culling cannot selectively hide Default, so ShowNotes/ShowUI
    // stopped working for those objects. This pass moves the strays back to
    // their cullable layers:
    //   note/rail manager subtrees: Default -> Notes
    //   [Score UI] subtree:         Default -> StageUI
    //
    // Safe by construction: the headset camera already renders Notes and
    // StageUI, so the HMD view is unchanged, and the game's note physics
    // already lives on the Notes layer. Runs only when an enabled camera
    // actually hides notes or UI; otherwise the game hierarchy is never
    // touched. Re-run periodically in-game to catch pool growth.
    public static class LayerNormalizer
    {
        private static readonly string[] NoteRootCandidates = new string[]
        {
            "Note Manager(Clone)", "Rail Manager(Clone)"
        };
        private static readonly string[] UiRootCandidates = new string[]
        {
            "[Score UI]"
        };

        // Returns number of objects re-layered (0 when roots/layers missing).
        public static int NormalizeNotes()
        {
            int target = LayerMask.NameToLayer("Notes");
            if (target < 0)
                return 0;
            int moved = 0;
            for (int i = 0; i < NoteRootCandidates.Length; i++)
                moved += NormalizeRoot(NoteRootCandidates[i], target);
            return moved;
        }

        public static int NormalizeUi()
        {
            int target = LayerMask.NameToLayer("StageUI");
            if (target < 0)
                return 0;
            int moved = 0;
            for (int i = 0; i < UiRootCandidates.Length; i++)
                moved += NormalizeRoot(UiRootCandidates[i], target);
            return moved;
        }

        private static int NormalizeRoot(string rootName, int targetLayer)
        {
            try
            {
                GameObject root = GameObject.Find(rootName);
                if (root == null)
                    return 0;
                return MoveDefaultChildren(root.transform, targetLayer, 0);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // Move every object on Default (layer 0) within the subtree to the
        // target layer. Other layers are left untouched -- the game's own
        // layering (HideInMainScreen etc.) stays intact.
        private static int MoveDefaultChildren(Transform t, int targetLayer, int depth)
        {
            if (t == null || depth > 12)
                return 0;

            int moved = 0;
            GameObject go = t.gameObject;
            if (go.layer == 0)
            {
                go.layer = targetLayer;
                moved++;
            }

            int n = t.childCount;
            for (int i = 0; i < n; i++)
                moved += MoveDefaultChildren(t.GetChild(i), targetLayer, depth + 1);
            return moved;
        }
    }
}
