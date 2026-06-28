using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ModularSaveManager))]
public sealed class ModularSaveManagerInspector : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("基础配置", EditorStyles.boldLabel);
        DrawPropertiesExcluding(serializedObject, "m_Script");

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8f);
        DrawRuntimeInfo((ModularSaveManager)target);
    }

    private static void DrawRuntimeInfo(ModularSaveManager manager)
    {
        EditorGUILayout.LabelField("运行时状态", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawReadonly("当前槽位", manager.ActiveSlot.ToString());
            DrawReadonly("已加载", manager.IsLoaded ? "是" : "否");
            DrawReadonly("有脏数据", manager.IsDirty ? "是" : "否");
            DrawReadonly("加密存档", manager.EncryptSaveFile ? "开启" : "关闭");
            DrawReadonly("模块数量", manager.RegisteredModuleCount.ToString());
            DrawReadonly("迁移器数量", manager.RegisteredMigrationCount.ToString());
            DrawReadonly("当前路径", manager.ActiveSlotPath);
        }

        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = Application.isPlaying;

            if (GUILayout.Button("自动注册"))
            {
                manager.AutoRegisterAll();
            }

            if (GUILayout.Button("读取当前槽"))
            {
                manager.LoadSlot(manager.ActiveSlot);
            }

            if (GUILayout.Button("立即保存"))
            {
                manager.SaveNow();
            }

            GUI.enabled = true;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("打开调试面板"))
            {
                ModularSaveManagerDebugWindow.Open();
            }

            if (GUILayout.Button("打开存档目录"))
            {
                string directory = Path.GetDirectoryName(manager.ActiveSlotPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    EditorUtility.RevealInFinder(directory);
                }
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("读取、保存、自动注册按钮只在 Play Mode 下可用。", MessageType.Info);
        }
    }

    private static void DrawReadonly(string label, string value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(80f));
            EditorGUILayout.SelectableLabel(value ?? string.Empty, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }
    }
}
