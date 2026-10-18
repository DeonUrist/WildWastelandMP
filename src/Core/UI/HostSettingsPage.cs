using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildWasteland.Core.UI
{
    internal sealed class HostSettingsPage : ToolPage
    {
        readonly bool _steam, _title;
        readonly List<MultiplayerHosting.SaveChoice> _saves;
        string _name, _seed, _error;
        int _source, _selected = -1;
        readonly LedgerDropdown _cars = new LedgerDropdown();
        readonly List<KeyValuePair<string, string>> _carOptions = new List<KeyValuePair<string, string>>();
        string _car = "RANDOM";
        Rect _carAnchor;
        bool _openCars;
        internal HostSettingsPage(bool steam)
        {
            Title = "HOST WORLD"; _steam = steam; _title = Multiplayer.InMainMenu;
            _name = _title ? "" : System.IO.Path.GetFileNameWithoutExtension(MultiplayerHosting.CurrentFile ?? "World");
            _seed = UnityEngine.Random.Range(1, 1000000).ToString();
            _saves = _title ? MultiplayerHosting.Saves() : new List<MultiplayerHosting.SaveChoice>();
            _error = MultiplayerHosting.Error;
            foreach (string car in MultiplayerHosting.StarterCars) _carOptions.Add(new KeyValuePair<string, string>(car == "RANDOM" ? "" : car, car));
        }
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
                    for (int i = 0; i < _saves.Count; i++)
                        if (l.Button((i == _selected ? "> " : "") + _saves[i].Name + "  (" + _saves[i].File + ")"))
                        { _selected = i; _name = _saves[i].Name; }
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
