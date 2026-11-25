using System;
using System.Collections.Generic;
using System.IO;
using UnityEditorInternal;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// 楽器プリセットの一覧（内蔵＋ユーザー保存）。ユーザー分は Unity の設定フォルダの JSON に置くので、プロジェクトをまたいで使える。
    /// </summary>
    public static class InstrumentPresets
    {
        [Serializable]
        class FileBody
        {
            public List<InstrumentPreset> presets = new List<InstrumentPreset>();
        }

        /// <summary>保存先（テストで差し替える）。</summary>
        public static string FilePath = Path.Combine(InternalEditorUtility.unityPreferencesFolder, "UniDAW", "InstrumentPresets.json");

        public static List<InstrumentPreset> LoadUser()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<InstrumentPreset>();
                var body = JsonUtility.FromJson<FileBody>(File.ReadAllText(FilePath));
                var list = body?.presets ?? new List<InstrumentPreset>();
                list.RemoveAll(p => p?.instrument == null);
                foreach (var p in list)
                {
                    p.user = true;
                    p.instrument.EnsureShape();
                }
                return list;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UniDAW] 楽器プリセットを読めませんでした: " + e.Message);
                return new List<InstrumentPreset>();
            }
        }

        /// <summary>内蔵 → ユーザー保存の順。</summary>
        public static List<InstrumentPreset> All()
        {
            var list = new List<InstrumentPreset>(InstrumentLibrary.BuiltIn);
            list.AddRange(LoadUser());
            return list;
        }

        /// <summary>楽器をユーザープリセットとして保存（同じ名前なら上書き）。</summary>
        public static void Save(Instrument inst, string category = "User")
        {
            if (inst == null) return;
            var list = LoadUser();
            list.RemoveAll(p => p.Name == inst.name);
            list.Add(new InstrumentPreset(category, inst.Clone(), true));
            Write(list);
        }

        public static void Delete(string name)
        {
            var list = LoadUser();
            if (list.RemoveAll(p => p.Name == name) > 0) Write(list);
        }

        /// <summary>一時ファイルに書いてから置き換える（書きかけで壊さない）。</summary>
        static void Write(List<InstrumentPreset> list)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(new FileBody { presets = list }, true));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
    }
}
