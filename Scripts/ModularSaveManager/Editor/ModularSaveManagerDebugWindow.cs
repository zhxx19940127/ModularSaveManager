using System;
using System.Collections.Generic;
using System.IO;
using LitJson;
using UnityEditor;
using UnityEngine;

public sealed class ModularSaveManagerDebugWindow : EditorWindow
{
    private Vector2 _scroll;
    private int _slot;
    private string _jsonPreview = string.Empty;
    private string _statusMessage = string.Empty;

    [MenuItem("Tools/存档管理器/调试面板")]
    public static void Open()
    {
        ModularSaveManagerDebugWindow window = GetWindow<ModularSaveManagerDebugWindow>("存档调试");
        window.minSize = new Vector2(520f, 520f);
        window.Show();
    }

    private void OnGUI()
    {
        ModularSaveManager manager = ModularSaveManager.Instance;

        DrawToolbar(manager);
        EditorGUILayout.Space(8f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawRuntimeState(manager);
        EditorGUILayout.Space(8f);
        DrawSlotTools(manager);
        EditorGUILayout.Space(8f);
        DrawModules(manager);
        EditorGUILayout.Space(8f);
        DrawMigrations(manager);
        EditorGUILayout.Space(8f);
        DrawJsonPreview();
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar(ModularSaveManager manager)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("模块化存档调试", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();

        GUI.enabled = manager != null;
        if (GUILayout.Button("选中管理器", EditorStyles.toolbarButton, GUILayout.Width(90f)))
        {
            Selection.activeObject = manager.gameObject;
            EditorGUIUtility.PingObject(manager.gameObject);
        }

        GUI.enabled = true;
        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60f)))
        {
            Repaint();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawRuntimeState(ModularSaveManager manager)
    {
        EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);

        if (manager == null)
        {
            EditorGUILayout.HelpBox("场景中没有运行中的 ModularSaveManager。请进入 Play Mode，或确认场景里有挂载该组件。", MessageType.Warning);
            return;
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawReadonly("当前槽位", manager.ActiveSlot.ToString());
            DrawReadonly("已加载", manager.IsLoaded ? "是" : "否");
            DrawReadonly("有脏数据", manager.IsDirty ? "是" : "否");
            DrawReadonly("存档加密", manager.EncryptSaveFile ? "开启" : "关闭");
            DrawReadonly("模块数量", manager.RegisteredModuleCount.ToString());
            DrawReadonly("迁移器数量", manager.RegisteredMigrationCount.ToString());
            DrawReadonly("当前槽路径", manager.ActiveSlotPath);
        }
    }

    private void DrawSlotTools(ModularSaveManager manager)
    {
        EditorGUILayout.LabelField("槽位工具", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _slot = EditorGUILayout.IntField("槽位编号", Mathf.Max(0, _slot));

            GUI.enabled = manager != null;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("读取槽位"))
            {
                manager.LoadSlot(_slot);
                _statusMessage = $"已读取槽位 {_slot}";
            }

            if (GUILayout.Button("保存到槽位"))
            {
                manager.SaveSlot(_slot);
                _statusMessage = $"已保存到槽位 {_slot}";
            }

            if (GUILayout.Button("立即保存当前槽"))
            {
                manager.SaveNow();
                _statusMessage = "已立即保存当前槽";
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("删除槽位"))
            {
                if (EditorUtility.DisplayDialog("删除存档槽", $"确定删除槽位 {_slot} 的存档、临时文件和备份文件吗？", "删除", "取消"))
                {
                    manager.DeleteSlot(_slot);
                    _jsonPreview = string.Empty;
                    _statusMessage = $"已删除槽位 {_slot}";
                }
            }

            if (GUILayout.Button("预览 JSON"))
            {
                LoadJsonPreview(manager, _slot);
            }

            if (GUILayout.Button("打开存档目录"))
            {
                OpenSaveDirectory(manager, _slot);
            }

            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }
        }
    }

    private void DrawModules(ModularSaveManager manager)
    {
        EditorGUILayout.LabelField("已注册模块", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (manager == null)
            {
                EditorGUILayout.LabelField("无运行中管理器");
                return;
            }

            List<SaveModule> modules = manager.GetRegisteredModulesSnapshot();
            if (modules.Count == 0)
            {
                EditorGUILayout.LabelField("暂无已注册模块");
                return;
            }

            for (int i = 0; i < modules.Count; i++)
            {
                SaveModule module = modules[i];
                if (module == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawReadonly("Key", module.Key);
                DrawReadonly("版本", module.Version.ToString());
                DrawReadonly("类型", module.GetType().FullName);
                EditorGUILayout.EndVertical();
            }
        }
    }

    private void DrawMigrations(ModularSaveManager manager)
    {
        EditorGUILayout.LabelField("已注册迁移器", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (manager == null)
            {
                EditorGUILayout.LabelField("无运行中管理器");
                return;
            }

            List<ISaveMigration> migrations = manager.GetRegisteredMigrationsSnapshot();
            if (migrations.Count == 0)
            {
                EditorGUILayout.LabelField("暂无已注册迁移器");
                return;
            }

            for (int i = 0; i < migrations.Count; i++)
            {
                ISaveMigration migration = migrations[i];
                if (migration == null)
                {
                    continue;
                }

                EditorGUILayout.LabelField(
                    $"{migration.Key}  v{migration.FromVersion} -> v{migration.ToVersion}",
                    migration.GetType().FullName);
            }
        }
    }

    private void DrawJsonPreview()
    {
        EditorGUILayout.LabelField("JSON 预览", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (string.IsNullOrEmpty(_jsonPreview))
            {
                EditorGUILayout.LabelField("暂无预览内容");
                return;
            }

            EditorGUILayout.TextArea(_jsonPreview, GUILayout.MinHeight(180f));
        }
    }

    private void LoadJsonPreview(ModularSaveManager manager, int slot)
    {
        string path = manager.GetSlotPath(slot);
        if (!File.Exists(path))
        {
            _jsonPreview = string.Empty;
            _statusMessage = $"槽位 {slot} 没有存档文件";
            return;
        }

        try
        {
            string content = File.ReadAllText(path);
            _jsonPreview = TryFormatJson(content);
            _statusMessage = $"已预览槽位 {slot}";
        }
        catch (Exception exception)
        {
            _jsonPreview = string.Empty;
            _statusMessage = $"读取 JSON 失败: {exception.Message}";
        }
    }

    private static string TryFormatJson(string content)
    {
        if (SaveCrypto.IsEncryptedText(content))
        {
            return "当前存档已加密，调试面板不会显示明文内容。";
        }

        try
        {
            JsonData data = JsonMapper.ToObject(content);
            return data.ToJson();
        }
        catch
        {
            return content;
        }
    }

    private static void OpenSaveDirectory(ModularSaveManager manager, int slot)
    {
        string path = manager.GetSlotPath(slot);
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            EditorUtility.RevealInFinder(directory);
        }
    }

    private static void DrawReadonly(string label, string value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(90f));
            EditorGUILayout.SelectableLabel(value ?? string.Empty, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }
    }
}
