using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Core.UI
{
    internal sealed class HostSettingsPage : ToolPage
    {
        readonly bool _steam, _title;
        readonly List<HostSaveSystem.SaveInfo> _saves = new List<HostSaveSystem.SaveInfo>();
        string _name, _seed, _error;
        int _source, _saveKind, _savePage, _selected = -1;
        readonly LedgerDropdown _cars = new LedgerDropdown();
        readonly List<KeyValuePair<string, string>> _carOptions = new List<KeyValuePair<string, string>>();
        string _car = "RANDOM";
        Rect _carAnchor;
        bool _openCars;
        internal HostSettingsPage(bool steam, string selectedFile = null)
        {
            Title = "HOST WORLD"; _steam = steam; _title = Multiplayer.InMainMenu;
            _name = _title ? "" : HostSaveSystem.CurrentWorldName;
            _seed = UnityEngine.Random.Range(1, 1000000).ToString();
            HostSavePage.Instance.ReleasePictures();
            if (_title) foreach (var save in MultiplayerHosting.Saves()) _saves.Add(HostSaveSystem.Info(save.File, save.Name));
            if (selectedFile != null) for (int i = 0; i < _saves.Count; i++) if (_saves[i].File == selectedFile) { _source = 1; _saveKind = _saves[i].Automatic ? 1 : 0; _selected = i; _name = SavedWorldName(_saves[i]); for (int j = 0; j < i; j++) if (_saves[j].Automatic == _saves[i].Automatic) _savePage++; _savePage /= 5; break; }
            _error = MultiplayerHosting.Error;
            foreach (string car in MultiplayerHosting.StarterCars) _carOptions.Add(new KeyValuePair<string, string>(car == "RANDOM" ? "" : car, car));
        }
        static string SavedWorldName(HostSaveSystem.SaveInfo save)
        { return save.World == "World not recorded" ? save.Name : save.World; }
        public override void Draw(ToolLayout l)
        {
            l.Text(_steam ? "Host through Steam" : "Host through UDP");
            _name = l.TextInput("World name", "Name", _name, 32);
            if (_title)
            {
                _source = l.Choices("World", _source, "New world", "Existing save");
                if (_source == 0)
                {
                    _seed = l.TextInput("Seed", "Seed", _seed, 11);
                    var row = l.Next(); Ledger.RowHighlight(row);
                    Ledger.Label(new Rect(row.x + 22, row.y, row.width * ToolLayout.LabelWidth - 22, row.height), "Starting vehicle");
                    var field = new Rect(row.x + row.width * ToolLayout.LabelWidth, row.y, row.width * (1 - ToolLayout.LabelWidth), row.height);
                    if (Ledger.Dropdown(field, _car)) { _carAnchor = new Rect(GUIUtility.GUIToScreenPoint(field.position), field.size); _openCars = true; }
                }
                else
                {
                    if (_saves.Count == 0) l.Text("No saved worlds are available.");
                    int kind = l.Choices("Show", _saveKind, "Manual saves", "Autosaves");
                    if (kind != _saveKind) { _saveKind = kind; _savePage = 0; _selected = -1; }
                    l.Text("Players is the saved total, including the host and sleeping players.");
                    HostSavePage.SaveColumns(l.Next(32), true);
                    int count = 0;
                    for (int i = 0; i < _saves.Count; i++)
                        if (_saves[i].Automatic == (_saveKind == 1))
                        {
                            int row = count++; if (row < _savePage * 5 || row >= (_savePage + 1) * 5) continue;
                            if (HostSavePage.Instance.SaveRow(l.Next(96), _saves[i], i == _selected))
                            { _selected = i; _name = SavedWorldName(_saves[i]); }
                        }
                    l.Text((_savePage + 1) + " / " + Mathf.Max(1, (count + 4) / 5) + "    ·    " + count + " saves");
                    int page = l.Buttons("PREVIOUS", "NEXT");
                    if (page == 0 && _savePage > 0) _savePage--; else if (page == 1 && (_savePage + 1) * 5 < count) _savePage++;
                }
            }
            else l.Value("Save slot", MultiplayerHosting.CurrentFile ?? "Unavailable");
            if (_error != null) l.Text(_error, Ledger.Danger);
            CorePages.DrawWorld(l);
        }
        internal void Before() { _cars.Before(); }
        internal void DrawPopup()
        {
            if (_openCars) { _cars.Open(new Rect(GUIUtility.ScreenToGUIPoint(_carAnchor.position), _carAnchor.size), _carOptions, _car == "RANDOM" ? "" : _car, SetCar, 884); _openCars = false; }
            _cars.Draw(); _cars.Flush();
        }
        void SetCar(string car) { _car = string.IsNullOrEmpty(car) ? "RANDOM" : car; }
        internal void DrawActions(Rect row)
        {
            float width = (row.width - 16) / 2;
            if (Ledger.Button(new Rect(row.x, row.y, width, row.height), "HOST WORLD", Multiplayer.CanHost))
            {
                string file = _title && _source == 1 && _selected >= 0 ? _saves[_selected].File : null;
                _error = _title && _source == 1 && _selected < 0 ? "Choose an existing save." : MultiplayerHosting.Validate(_name, file, _seed);
                if (_error == null)
                {
                    string name = _name, seed = _seed;
                    ToolWindow.CloseThen(() => MultiplayerHosting.Start(name, file, seed, _steam, _car));
                }
            }
            if (Ledger.Button(new Rect(row.x + width + 16, row.y, width, row.height), "CANCEL")) ToolWindow.Close();
        }
    }
}
