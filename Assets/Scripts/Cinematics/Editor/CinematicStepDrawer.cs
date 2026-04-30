#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MWI.Cinematics.EditorTools
{
    /// <summary>
    /// Property drawer for any field referencing a <see cref="CinematicStep"/> via
    /// <c>[SerializeReference]</c> (typically <c>CinematicSceneSO._steps</c>).
    ///
    /// Phase 1 helper. Renders a type-picker dropdown inline next to the foldout so
    /// designers can pick a concrete step type (Speak / Wait / Move / Trigger) when
    /// adding new entries to a step list — Unity's default SerializeReference UX hides
    /// this dropdown behind right-click context menus and is easy to miss.
    ///
    /// Phase 4 will replace this with the full Cinematic Scene Editor window per spec
    /// §12. Until then, this drawer is the only authoring surface for designers.
    /// </summary>
    [CustomPropertyDrawer(typeof(CinematicStep), useForChildren: true)]
    public class CinematicStepDrawer : PropertyDrawer
    {
        private const float DropdownWidth = 170f;
        private const float DropdownPadding = 4f;

        private static Type[] s_StepTypes;
        private static string[] s_StepTypeLabels;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EnsureTypeCache();
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;

            // Header row: foldout / "(none)" label on the left, type dropdown on the right.
            Rect headerRect = new Rect(position.x, position.y, position.width, line);
            Rect dropdownRect = new Rect(headerRect.xMax - DropdownWidth, headerRect.y,
                                         DropdownWidth, line);
            Rect foldoutRect = new Rect(headerRect.x, headerRect.y,
                                        headerRect.width - DropdownWidth - DropdownPadding, line);

            int currentIndex = GetCurrentTypeIndex(property);
            object value = property.managedReferenceValue;

            if (value != null)
            {
                string typeName = value.GetType().Name;
                property.isExpanded = EditorGUI.Foldout(
                    foldoutRect, property.isExpanded,
                    $"{label.text}  ({typeName})", true);
            }
            else
            {
                EditorGUI.LabelField(foldoutRect, $"{label.text}  (no step type set)");
                property.isExpanded = false;
            }

            int newIndex = EditorGUI.Popup(dropdownRect, currentIndex, s_StepTypeLabels);
            if (newIndex != currentIndex)
            {
                Undo.RecordObject(property.serializedObject.targetObject, "Change Cinematic Step Type");
                if (newIndex == 0)
                {
                    property.managedReferenceValue = null;
                    property.isExpanded = false;
                }
                else
                {
                    Type targetType = s_StepTypes[newIndex - 1];
                    property.managedReferenceValue = Activator.CreateInstance(targetType);
                    property.isExpanded = true;
                }
                property.serializedObject.ApplyModifiedProperties();
            }

            // Draw inner fields if expanded and value is set. Iterate children manually
            // to avoid the drawer recursing on itself when calling PropertyField on the
            // base property.
            if (property.isExpanded && property.managedReferenceValue != null)
            {
                EditorGUI.indentLevel++;
                float y = position.y + line + 2;

                SerializedProperty endProp = property.GetEndProperty();
                SerializedProperty iter = property.Copy();
                bool enterChildren = true;
                while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, endProp))
                {
                    enterChildren = false;
                    float h = EditorGUI.GetPropertyHeight(iter, true);
                    Rect r = new Rect(position.x, y, position.width, h);
                    EditorGUI.PropertyField(r, iter, true);
                    y += h + 2;
                }
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            EnsureTypeCache();

            float h = EditorGUIUtility.singleLineHeight + 2;

            if (property.isExpanded && property.managedReferenceValue != null)
            {
                SerializedProperty endProp = property.GetEndProperty();
                SerializedProperty iter = property.Copy();
                bool enterChildren = true;
                while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, endProp))
                {
                    enterChildren = false;
                    h += EditorGUI.GetPropertyHeight(iter, true) + 2;
                }
            }

            return h;
        }

        private static void EnsureTypeCache()
        {
            if (s_StepTypes != null) return;

            // Discover every concrete CinematicStep subclass across loaded assemblies.
            // Lazy-cached for the editor session — domain reload re-runs this.
            var concreteTypes = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }    // ReflectionTypeLoadException — partial loads OK

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.IsAbstract) continue;
                    if (!typeof(CinematicStep).IsAssignableFrom(t)) continue;
                    if (t == typeof(CinematicStep)) continue;
                    concreteTypes.Add(t);
                }
            }

            concreteTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            s_StepTypes = concreteTypes.ToArray();

            s_StepTypeLabels = new string[s_StepTypes.Length + 1];
            s_StepTypeLabels[0] = "<none>";
            for (int i = 0; i < s_StepTypes.Length; i++)
                s_StepTypeLabels[i + 1] = s_StepTypes[i].Name;
        }

        private static int GetCurrentTypeIndex(SerializedProperty property)
        {
            object val = property.managedReferenceValue;
            if (val == null) return 0;

            Type t = val.GetType();
            for (int i = 0; i < s_StepTypes.Length; i++)
            {
                if (s_StepTypes[i] == t) return i + 1;
            }
            return 0;
        }
    }
}
#endif
