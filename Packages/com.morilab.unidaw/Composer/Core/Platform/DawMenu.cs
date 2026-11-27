using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniDAW.Composer
{
    /// <summary>
    /// ポップアップメニューの中身（項目の並びだけを持ち、出し方はホストの <see cref="IDawPlatform.ShowMenu"/> に任せる）。
    /// パスの "/" はサブメニューの区切り。
    /// </summary>
    public sealed class DawMenu
    {
        public readonly struct Entry
        {
            public readonly string path;
            public readonly Action action;
            public readonly bool on, enabled, separator;

            public Entry(string path, Action action, bool on, bool enabled, bool separator)
            {
                this.path = path ?? "";
                this.action = action;
                this.on = on;
                this.enabled = enabled;
                this.separator = separator;
            }
        }

        readonly List<Entry> entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => entries;

        public void AddItem(string path, bool on, Action action) => entries.Add(new Entry(path, action, on, action != null, false));
        public void AddDisabledItem(string path, bool on = false) => entries.Add(new Entry(path, null, on, false, false));
        public void AddSeparator(string path) => entries.Add(new Entry(path, null, false, false, true));

        /// <summary>有効なら項目、無効なら灰色の項目。</summary>
        public void Item(string path, bool enabled, Action action, bool on = false)
        {
            if (enabled && action != null) AddItem(path, on, action);
            else AddDisabledItem(path, on);
        }

        public void DropDown(Rect worldRect) => DawPlatform.Current.ShowMenu(this, worldRect);
        public void ShowAsContext() => DawPlatform.Current.ShowMenu(this, null);
    }
}
