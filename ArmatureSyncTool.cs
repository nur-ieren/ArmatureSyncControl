#if UNITY_EDITOR
// Unity 2022.3.22f1 / Assets/nur_ieren/ArmatureSyncTool.cs
// Menu: nur_ieren > Armature Sync Tool   (メニュー: nur_ieren > Armature Sync Tool)
// Wrapped in #if UNITY_EDITOR so it works outside an Editor folder.
// UnityEditor を使うため、Editorフォルダ外でも動くよう全体を #if UNITY_EDITOR で囲んでいます。
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace nur_ieren
{
    public class ArmatureSyncTool : EditorWindow
    {
        private const string PhysBoneNamespace = "VRC.SDK3.Dynamics.PhysBone.Components";
        private const string LangPrefKey = "nur_ieren.ArmatureSyncTool.Language";

        // 0 = 日本語, 1 = English
        private static int _lang;

        private GameObject _reference;   // 参考にする側 / Reference
        private GameObject _target;      // 適応する側 / Target

        private bool _syncTransform = true;
        private bool _syncActive = true;
        private bool _syncTag = true;
        private bool _syncPhysBone = true;
        private bool _syncOther = true;
        private bool _syncBlendShape = true;
        private bool _removeMaShapeComponents = true;

        private List<Transform> _onlyInTarget = new List<Transform>();
        private List<Excluded> _excluded = new List<Excluded>();
        private bool _showExcluded;
        private bool _listed;
        private Vector2 _scroll;

        // 日本語 / English を切り替えて返す
        private static string T(string ja, string en)
        {
            return _lang == 0 ? ja : en;
        }

        [MenuItem("nur_ieren/Armature Sync Tool")]
        private static void Open()
        {
            var w = GetWindow<ArmatureSyncTool>("Armature Sync");
            w.minSize = new Vector2(380, 480);
        }

        private void OnEnable()
        {
            _lang = EditorPrefs.GetInt(LangPrefKey, 0);
        }

        // ------------------------------------------------------------------ GUI
        private void OnGUI()
        {
            int newLang = GUILayout.Toolbar(_lang, new[] { "日本語", "English" });
            if (newLang != _lang)
            {
                _lang = newLang;
                EditorPrefs.SetInt(LangPrefKey, _lang);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Armature Sync Tool", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                T("同名ボーンを参考側 → 適応側へ揃えます。（Undo可）",
                  "Syncs bones with the same name from Reference → Target. (Undo supported)"),
                MessageType.Info);

            _reference = (GameObject)EditorGUILayout.ObjectField(T("参考にするArmature", "Reference Armature"), _reference, typeof(GameObject), true);
            _target = (GameObject)EditorGUILayout.ObjectField(T("適応するArmature", "Target Armature"), _target, typeof(GameObject), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(T("同期する項目", "Items to sync"), EditorStyles.boldLabel);
            _syncTransform = EditorGUILayout.ToggleLeft(T("Transform (Position / Rotation / Scale)", "Transform (Position / Rotation / Scale)"), _syncTransform);
            _syncActive = EditorGUILayout.ToggleLeft(T("チェックマーク (Active)", "Checkmark (Active)"), _syncActive);
            _syncTag = EditorGUILayout.ToggleLeft("Tag", _syncTag);
            _syncPhysBone = EditorGUILayout.ToggleLeft(
                T("PhysBone / PhysBoneCollider (追加・削除・値コピー)", "PhysBone / PhysBoneCollider (add / remove / copy values)"), _syncPhysBone);
            _syncOther = EditorGUILayout.ToggleLeft(
                T("その他のコンポーネント (MA Scale Adjuster等 / 追加・値コピー)", "Other components (e.g. MA Scale Adjuster / add / copy values)"), _syncOther);
            _syncBlendShape = EditorGUILayout.ToggleLeft(
                T("Armatureと同階層の同名ObjectのBlendShape (SkinnedMeshRenderer)", "BlendShapes of same-named objects beside the Armature (SkinnedMeshRenderer)"), _syncBlendShape);
            using (new EditorGUI.DisabledScope(!_syncBlendShape))
            {
                EditorGUI.indentLevel++;
                _removeMaShapeComponents = EditorGUILayout.ToggleLeft(
                    T("先に適応側の MA Blendshape Sync / MA Shape Changer を削除", "First remove MA Blendshape Sync / MA Shape Changer on the Target"), _removeMaShapeComponents);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            string error = Validate();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);

            using (new EditorGUI.DisabledScope(error != null))
            {
                if (GUILayout.Button(
                        T("実行（同期 + 適応側にしかないボーンをリスト化）", "Run (sync + list bones that only exist on the Target)"),
                        GUILayout.Height(30)))
                {
                    Apply();
                    BuildList();
                }
                if (GUILayout.Button(T("リストだけ更新", "Refresh list only")))
                {
                    BuildList();
                }
            }

            DrawList();
        }

        private void DrawList()
        {
            if (!_listed) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                T($"参考側に無いボーン: {_onlyInTarget.Count} 件", $"Bones not in Reference: {_onlyInTarget.Count}"),
                EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(_onlyInTarget.Count == 0))
            {
                if (GUILayout.Button(T("リスト全てをHierarchyで選択", "Select all listed in Hierarchy")))
                {
                    Selection.objects = _onlyInTarget.Where(t => t != null).Select(t => (UnityEngine.Object)t.gameObject).ToArray();
                }
            }

            if (_excluded.Count > 0)
            {
                _showExcluded = EditorGUILayout.Foldout(
                    _showExcluded,
                    T($"削除してはいけないため除外: {_excluded.Count} 件", $"Excluded (must not be deleted): {_excluded.Count}"),
                    true);
                if (_showExcluded)
                {
                    foreach (var ex in _excluded)
                    {
                        if (ex.T == null) continue;
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(new GUIContent(GetPath(ex.T, _target.transform), ex.Reason()), GUILayout.MinWidth(100));
                        if (GUILayout.Button(T("選択", "Select"), GUILayout.Width(60)))
                        {
                            Selection.activeGameObject = ex.T.gameObject;
                            EditorGUIUtility.PingObject(ex.T.gameObject);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var t in _onlyInTarget)
            {
                if (t == null) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(GetPath(t, _target.transform), GUILayout.MinWidth(100));
                if (GUILayout.Button(T("選択", "Select"), GUILayout.Width(60)))
                {
                    Selection.activeGameObject = t.gameObject;
                    EditorGUIUtility.PingObject(t.gameObject);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private string Validate()
        {
            if (_reference == null || _target == null)
                return T("参考側と適応側の両方を指定してください。", "Please assign both the Reference and the Target.");
            if (_reference == _target)
                return T("同じオブジェクトは指定できません。", "The same object cannot be used for both.");
            if (_target.transform.IsChildOf(_reference.transform) || _reference.transform.IsChildOf(_target.transform))
                return T("親子関係にあるオブジェクト同士は指定できません。", "Objects in a parent/child relationship cannot be used together.");
            return null;
        }

        // ------------------------------------------------------------ Matching
        private class Pair
        {
            public Transform Src;
            public Transform Dst;
        }

        private class Excluded
        {
            public Transform T;
            public bool IsPrefab;   // true: Armature内のPrefab / false: SkinnedMeshRendererが参照

            public string Reason()
            {
                return IsPrefab
                    ? ArmatureSyncTool.T("Armature内のPrefab（同期・リストの対象外）", "Prefab inside the Armature (excluded from sync and list)")
                    : ArmatureSyncTool.T("SkinnedMeshRendererが参照するBoneを含む（リストから除外）", "Contains a bone referenced by a SkinnedMeshRenderer (excluded from list)");
            }
        }

        // Armature内に置かれたPrefabインスタンス(中のArmatureやメッシュを含む)の一員か判定する。
        // 一番親のArmatureより内側にあるPrefabインスタンスのルートだけを「入れ子Prefab」とみなす。
        private static bool IsInNestedPrefab(Transform t, Transform armatureRoot, out Transform nestedRoot)
        {
            nestedRoot = null;
            if (t == armatureRoot) return false;
            var go = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
            if (go == null) return false;
            var r = go.transform;
            if (r == armatureRoot || !r.IsChildOf(armatureRoot)) return false;
            nestedRoot = r;
            return true;
        }

        private void BuildMatch(out List<Pair> pairs, out Dictionary<Transform, Transform> srcToDst, out List<Transform> unmatched, out List<Excluded> excluded)
        {
            pairs = new List<Pair>();
            srcToDst = new Dictionary<Transform, Transform>();
            unmatched = new List<Transform>();
            excluded = new List<Excluded>();

            Transform srcRoot = _reference.transform;
            Transform dstRoot = _target.transform;

            var srcByName = new Dictionary<string, List<Transform>>();
            foreach (var t in srcRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == srcRoot) continue; // 一番親のArmatureは名前に関係なく下で直接ペアにする
                if (IsInNestedPrefab(t, srcRoot, out _)) continue; // 参考側のArmature内Prefabは対象外
                if (!srcByName.TryGetValue(t.name, out var list))
                {
                    list = new List<Transform>();
                    srcByName[t.name] = list;
                }
                list.Add(t);
            }

            foreach (var d in dstRoot.GetComponentsInChildren<Transform>(true))
            {
                // 一番親のArmature同士は「Armature」「Armature.001」など接尾辞が違っても同じものとして扱う。
                // （Hips以下にある別のArmatureという名前のボーンは、通常どおり名前で対応づける）
                if (d == dstRoot)
                {
                    pairs.Add(new Pair { Src = srcRoot, Dst = dstRoot });
                    srcToDst[srcRoot] = dstRoot;
                    continue;
                }

                // Armature内のPrefab（中のArmature・メッシュ含む）は同期・削除候補の対象外にする
                if (IsInNestedPrefab(d, dstRoot, out var nestedRoot))
                {
                    if (nestedRoot == d)
                        excluded.Add(new Excluded { T = d, IsPrefab = true });
                    continue;
                }

                if (!srcByName.TryGetValue(d.name, out var cands))
                {
                    unmatched.Add(d);
                    continue;
                }

                Transform s = cands[0];
                if (cands.Count > 1)
                {
                    string path = GetPath(d, dstRoot);
                    var hit = cands.FirstOrDefault(x => GetPath(x, srcRoot) == path);
                    if (hit != null) s = hit;
                    else Debug.LogWarning(T(
                        $"[ArmatureSync] 参考側に同名ボーンが複数あります: '{d.name}' → 先頭を使用します。",
                        $"[ArmatureSync] Multiple bones named '{d.name}' in Reference → using the first one."));
                }

                pairs.Add(new Pair { Src = s, Dst = d });
                if (!srcToDst.ContainsKey(s)) srcToDst[s] = d;
            }

            // SkinnedMeshRenderer が Bone / RootBone として参照しているもの（とその親）は、
            // 削除すると参照が全て失われるため、リストから除外する。
            Transform scope = dstRoot.root;
            if (scope == srcRoot.root) scope = dstRoot;

            var referenced = new HashSet<Transform>();
            void AddWithAncestors(Transform tr)
            {
                for (; tr != null; tr = tr.parent) referenced.Add(tr);
            }
            foreach (var smr in scope.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                AddWithAncestors(smr.rootBone);
                foreach (var b in smr.bones) AddWithAncestors(b);
            }

            var unmatchedSet = new HashSet<Transform>(unmatched);
            var protectedSet = new HashSet<Transform>();
            foreach (var u in unmatched)
            {
                for (var a = u; a != null && a != dstRoot; a = a.parent)
                {
                    if (unmatchedSet.Contains(a) && referenced.Contains(a))
                    {
                        protectedSet.Add(u);
                        break;
                    }
                }
            }

            foreach (var u in protectedSet)
            {
                if (u.parent == null || !protectedSet.Contains(u.parent))
                    excluded.Add(new Excluded { T = u, IsPrefab = false });
            }
            unmatched.RemoveAll(u => protectedSet.Contains(u));
        }

        private void BuildList()
        {
            BuildMatch(out _, out _, out var unmatched, out var excluded);
            _onlyInTarget = unmatched;
            _excluded = excluded;
            _listed = true;
            Repaint();
        }

        // --------------------------------------------------------------- Apply
        private class RemapContext
        {
            public Transform RefScope;   // 参考側アバター(または参考Armature)
            public Transform DstScope;   // 適応側アバター(または適応Armature)
            public Dictionary<Transform, Transform> SrcToDst;
            public Dictionary<string, List<Transform>> DstByName;
        }

        private void Apply()
        {
            BuildMatch(out var pairs, out var srcToDst, out _, out _);

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Armature Sync");
            int group = Undo.GetCurrentGroup();

            int added = 0, removed = 0;

            // Pass 1: Transform / Active / Tag / コンポーネントの追加(PhysBone系は削除も)
            foreach (var p in pairs)
            {
                if (_syncTransform)
                {
                    Undo.RecordObject(p.Dst, "Armature Sync Transform");
                    p.Dst.localPosition = p.Src.localPosition;
                    p.Dst.localRotation = p.Src.localRotation;
                    p.Dst.localScale = p.Src.localScale;
                }

                if (_syncActive)
                {
                    Undo.RecordObject(p.Dst.gameObject, "Armature Sync Active");
                    p.Dst.gameObject.SetActive(p.Src.gameObject.activeSelf);
                }

                if (_syncTag)
                {
                    try
                    {
                        Undo.RecordObject(p.Dst.gameObject, "Armature Sync Tag");
                        p.Dst.gameObject.tag = p.Src.gameObject.tag;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(T(
                            $"[ArmatureSync] Tag設定失敗 '{p.Dst.name}': {e.Message}",
                            $"[ArmatureSync] Failed to set Tag on '{p.Dst.name}': {e.Message}"));
                    }
                }

                var srcByType = GetSyncComponents(p.Src).GroupBy(c => c.GetType()).ToDictionary(g => g.Key, g => g.Count());
                var allTypes = new HashSet<Type>(srcByType.Keys);
                if (_syncPhysBone)
                {
                    foreach (var c in GetSyncComponents(p.Dst))
                        if (IsPhysBoneType(c.GetType())) allTypes.Add(c.GetType());
                }

                foreach (var type in allTypes)
                {
                    int srcCount = srcByType.TryGetValue(type, out var n) ? n : 0;
                    var dstComps = p.Dst.GetComponents(type);

                    // 参考側に無い(or 少ない)PhysBone系は適応側から削除（それ以外のコンポーネントは削除しない）
                    if (IsPhysBoneType(type))
                    {
                        for (int i = dstComps.Length - 1; i >= srcCount; i--)
                        {
                            Undo.DestroyObjectImmediate(dstComps[i]);
                            removed++;
                        }
                    }

                    // 参考側にあって適応側に無いものは追加
                    for (int i = dstComps.Length; i < srcCount; i++)
                    {
                        var comp = Undo.AddComponent(p.Dst.gameObject, type);
                        if (comp == null)
                        {
                            Debug.LogWarning(T(
                                $"[ArmatureSync] コンポーネントを追加できませんでした: {type.Name} on '{p.Dst.name}'",
                                $"[ArmatureSync] Could not add component: {type.Name} on '{p.Dst.name}'"));
                            break;
                        }
                        added++;
                    }
                }
            }

            // Pass 2: 値コピー + 参照を適応側のオブジェクトへ付け替え
            if (_syncPhysBone || _syncOther)
            {
                var ctx = CreateContext(srcToDst);

                foreach (var p in pairs)
                {
                    foreach (var g in GetSyncComponents(p.Src).GroupBy(c => c.GetType()))
                    {
                        var srcComps = g.ToArray();
                        var dstComps = p.Dst.GetComponents(g.Key);
                        for (int i = 0; i < srcComps.Length && i < dstComps.Length; i++)
                        {
                            Undo.RecordObject(dstComps[i], "Armature Sync Component");
                            EditorUtility.CopySerialized(srcComps[i], dstComps[i]);
                            RemapReferences(dstComps[i], ctx);
                            EditorUtility.SetDirty(dstComps[i]);
                        }
                    }
                }
            }

            if (_syncBlendShape)
            {
                if (_removeMaShapeComponents) RemoveMaShapeComponents();
                SyncBlendShapes();
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log(T(
                $"[ArmatureSync] 完了: {pairs.Count} 個のボーンを同期 / コンポーネント 追加 {added} 件, 削除(PhysBone系) {removed} 件",
                $"[ArmatureSync] Done: synced {pairs.Count} bones / components added {added}, removed (PhysBone) {removed}"));
        }

        // BlendShape の値を上書きされないよう、適応側アバター内の
        // MA Blendshape Sync / MA Shape Changer を削除する（型名で判定するのでMA未導入でもコンパイル可）。
        private void RemoveMaShapeComponents()
        {
            Transform scope = _target.transform.root;
            if (scope == _reference.transform.root) scope = _target.transform;

            int removed = 0;
            foreach (var c in scope.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var type = c.GetType();
                if (type.Namespace == null || !type.Namespace.StartsWith("nadena.dev.modular_avatar")) continue;
                if (type.Name != "ModularAvatarBlendshapeSync" && type.Name != "ModularAvatarShapeChanger") continue;

                string label = $"{GetPath(c.transform, scope)} ({type.Name})";
                try
                {
                    Undo.DestroyObjectImmediate(c);
                    removed++;
                    Debug.Log(T($"[ArmatureSync] 削除: {label}", $"[ArmatureSync] Removed: {label}"));
                }
                catch (Exception e)
                {
                    Debug.LogWarning(T(
                        $"[ArmatureSync] 削除できませんでした（Prefab由来の可能性）: {label} / {e.Message}",
                        $"[ArmatureSync] Could not remove (may come from a Prefab): {label} / {e.Message}"));
                }
            }

            Debug.Log(T(
                $"[ArmatureSync] MA Blendshape Sync / Shape Changer を {removed} 件削除しました。",
                $"[ArmatureSync] Removed {removed} MA Blendshape Sync / Shape Changer component(s)."));
        }

        // Armatureと同じ階層(同じ親の直下)にある同名Objectの SkinnedMeshRenderer の BlendShape を揃える。
        // BlendShape は index ではなく名前で対応づける。
        private void SyncBlendShapes()
        {
            Transform refParent = _reference.transform.parent;
            Transform dstParent = _target.transform.parent;
            if (refParent == null || dstParent == null)
            {
                Debug.LogWarning(T(
                    "[ArmatureSync] ArmatureがルートにあるためBlendShape同期をスキップしました（同じ階層のObjectが存在しません）。",
                    "[ArmatureSync] Skipped BlendShape sync: the Armature is at the scene root (no sibling objects)."));
                return;
            }

            int meshCount = 0, shapeCount = 0;

            foreach (Transform dstSibling in dstParent)
            {
                if (dstSibling == _target.transform) continue;

                var dstSmr = dstSibling.GetComponent<SkinnedMeshRenderer>();
                if (dstSmr == null || dstSmr.sharedMesh == null) continue;

                Transform refSibling = null;
                foreach (Transform t in refParent)
                {
                    if (t == _reference.transform) continue;
                    if (t.name == dstSibling.name && t.GetComponent<SkinnedMeshRenderer>() != null)
                    {
                        refSibling = t;
                        break;
                    }
                }
                if (refSibling == null) continue;

                var refSmr = refSibling.GetComponent<SkinnedMeshRenderer>();
                if (refSmr.sharedMesh == null) continue;

                var refIndexByName = new Dictionary<string, int>();
                for (int i = 0; i < refSmr.sharedMesh.blendShapeCount; i++)
                    refIndexByName[refSmr.sharedMesh.GetBlendShapeName(i)] = i;

                Undo.RecordObject(dstSmr, "Armature Sync BlendShape");
                int copied = 0;
                for (int i = 0; i < dstSmr.sharedMesh.blendShapeCount; i++)
                {
                    string shapeName = dstSmr.sharedMesh.GetBlendShapeName(i);
                    if (refIndexByName.TryGetValue(shapeName, out int refIdx))
                    {
                        dstSmr.SetBlendShapeWeight(i, refSmr.GetBlendShapeWeight(refIdx));
                        copied++;
                    }
                }
                EditorUtility.SetDirty(dstSmr);

                meshCount++;
                shapeCount += copied;
                Debug.Log(T(
                    $"[ArmatureSync] BlendShape同期: '{dstSibling.name}' {copied}/{dstSmr.sharedMesh.blendShapeCount} 件",
                    $"[ArmatureSync] BlendShape sync: '{dstSibling.name}' {copied}/{dstSmr.sharedMesh.blendShapeCount}"));
            }

            Debug.Log(T(
                $"[ArmatureSync] BlendShape同期 完了: {meshCount} 個のメッシュ, 計 {shapeCount} 件",
                $"[ArmatureSync] BlendShape sync done: {meshCount} mesh(es), {shapeCount} shape(s) in total"));
        }

        private static bool IsPhysBoneType(Type t)
        {
            return t != null && t.FullName != null && t.FullName.StartsWith(PhysBoneNamespace);
        }

        // 同期対象のコンポーネント（Transform以外。PhysBone系/その他はトグルで切替）
        private IEnumerable<Component> GetSyncComponents(Transform t)
        {
            return t.GetComponents<Component>().Where(c =>
                c != null && !(c is Transform) &&
                (IsPhysBoneType(c.GetType()) ? _syncPhysBone : _syncOther));
        }

        // ------------------------------------------------------ Reference remap
        private RemapContext CreateContext(Dictionary<Transform, Transform> srcToDst)
        {
            // Armatureの親(アバタールート)があればそこまでを探索範囲にする。
            // Collider がArmature外(アバター直下の別オブジェクト等)にあっても同名/同位置で探せる。
            Transform refScope = _reference.transform.root;
            Transform dstScope = _target.transform.root;
            if (refScope == dstScope)
            {
                refScope = _reference.transform;
                dstScope = _target.transform;
            }

            var byName = new Dictionary<string, List<Transform>>();
            foreach (var t in dstScope.GetComponentsInChildren<Transform>(true))
            {
                if (!byName.TryGetValue(t.name, out var list))
                {
                    list = new List<Transform>();
                    byName[t.name] = list;
                }
                list.Add(t);
            }

            return new RemapContext { RefScope = refScope, DstScope = dstScope, SrcToDst = srcToDst, DstByName = byName };
        }

        // 参考側のTransformに対応する、適応側の候補を「同じ場所 → 同じパス → 同名」の順に返す
        private static IEnumerable<Transform> Candidates(Transform vt, RemapContext ctx)
        {
            if (ctx.SrcToDst.TryGetValue(vt, out var mapped)) yield return mapped;

            if (vt == ctx.RefScope)
            {
                yield return ctx.DstScope;
            }
            else
            {
                var byPath = ctx.DstScope.Find(GetPath(vt, ctx.RefScope));
                if (byPath != null) yield return byPath;
            }

            if (ctx.DstByName.TryGetValue(vt.name, out var list))
                foreach (var t in list) yield return t;
        }

        private static UnityEngine.Object ResolveReference(UnityEngine.Object v, Transform vt, RemapContext ctx)
        {
            if (v is Transform) return Candidates(vt, ctx).FirstOrDefault();
            if (v is GameObject) return Candidates(vt, ctx).Select(t => t.gameObject).FirstOrDefault();

            if (v is Component comp)
            {
                var type = comp.GetType();
                int idx = Array.IndexOf(vt.GetComponents(type), comp);
                foreach (var cand in Candidates(vt, ctx))
                {
                    var comps = cand.GetComponents(type);
                    if (comps.Length == 0) continue;
                    return (idx >= 0 && idx < comps.Length) ? comps[idx] : comps[0];
                }
            }
            return null;
        }

        private void RemapReferences(Component dstComp, RemapContext ctx)
        {
            var so = new SerializedObject(dstComp);
            var it = so.GetIterator();
            bool enter = true;
            var arraysToCompact = new HashSet<string>();
            const string arrayToken = ".Array.data[";

            while (it.NextVisible(enter))
            {
                enter = true;
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;

                var v = it.objectReferenceValue;
                if (v == null) continue;

                Transform vt = null;
                if (v is Component c) vt = c.transform;
                else if (v is GameObject go) vt = go.transform;
                if (vt == null) continue;                          // アセット等はそのまま

                if (vt.IsChildOf(ctx.DstScope)) continue;          // 既に適応側を指している
                if (!vt.IsChildOf(ctx.RefScope)) continue;         // どちらにも属さない外部参照はそのまま

                var newValue = ResolveReference(v, vt, ctx);
                if (newValue == null)
                {
                    Debug.LogWarning(T(
                        $"[ArmatureSync] 適応側に対応先が見つからず空にしました: {GetPath(dstComp.transform, ctx.DstScope)} / {it.propertyPath} → '{vt.name}'",
                        $"[ArmatureSync] No counterpart found on the Target, cleared: {GetPath(dstComp.transform, ctx.DstScope)} / {it.propertyPath} → '{vt.name}'"));

                    string path = it.propertyPath;
                    int pos = path.IndexOf(arrayToken, StringComparison.Ordinal);
                    if (pos >= 0 && path.EndsWith("]")) arraysToCompact.Add(path.Substring(0, pos));
                }

                it.objectReferenceValue = newValue;

                // Modular Avatar の AvatarObjectReference: referencePath(アバタールートからの相対パス)も合わせる
                if (newValue is GameObject ng && it.propertyPath.EndsWith("targetObject", StringComparison.Ordinal))
                {
                    string basePath = it.propertyPath.Substring(0, it.propertyPath.Length - "targetObject".Length);
                    var pathProp = so.FindProperty(basePath + "referencePath");
                    if (pathProp != null && pathProp.propertyType == SerializedPropertyType.String && ng.transform != ng.transform.root)
                        pathProp.stringValue = GetPath(ng.transform, ng.transform.root);
                }
            }

            // 配列(Collidersなど)内の空要素は詰める
            foreach (var arrPath in arraysToCompact)
            {
                var arr = so.FindProperty(arrPath);
                if (arr == null || !arr.isArray) continue;
                for (int i = arr.arraySize - 1; i >= 0; i--)
                {
                    var el = arr.GetArrayElementAtIndex(i);
                    if (el.propertyType == SerializedPropertyType.ObjectReference && el.objectReferenceValue == null)
                        arr.DeleteArrayElementAtIndex(i);
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string GetPath(Transform t, Transform root)
        {
            var names = new List<string>();
            while (t != null && t != root)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return names.Count == 0 ? root.name : string.Join("/", names);
        }
    }
}
#endif
