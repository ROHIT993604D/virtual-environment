﻿// LIZENZBEDINGUNGEN - Seanox Software Solutions ist ein Open-Source-Projekt, im
// Folgenden Seanox Software Solutions oder kurz Seanox genannt.
// Diese Software unterliegt der Version 2 der Apache License.
//
// Virtual Environment Launcher
// Program starter for the virtual environment.
// Copyright (C) 2022 Seanox Software Solutions
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not
// use this file except in compliance with the License. You may obtain a copy of
// the License at
//
// https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
// License for the specific language governing permissions and limitations under
// the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Seanox.Platform.Launcher.Tiles;

// TODO: Check usage dispose for a robust program
// TODO: Global mouse move event, then hide if outside -- for more screen usage
// TODO: Tiles: Matrix 4x10 with shortcuts from keyboard layout

namespace Seanox.Platform.Launcher
{
    // System Tray Icon (NotifyIcon) + menu for show + exit
    // ----
    // Omitted so that there is always a child process of the virtual
    // environment that can start other processes in the virtual environment.
    // In general, the launcher should run in the background as long as the
    // virtual environment IDE is active. Therefore there is no exit function.
    // The Launcher is terminated with the Detach of the virtual environment
    // via Kill -- sounds hard, but that's the concept.
    
    // Global HotKey / KeyEvent 
    // ----
    // At runtime, the launcher is opened via a system-wide HotKey combination.
    // The launcher is hidden when the focus is lost or the ESC key is pressed.
    
    // Navigation
    // ----
    // Mouse, arrow keys as well as tab and backslash are supported. In
    // combination with Shift inverts the behavior of the buttons.
    
    // Loss of focus
    // ----
    // The launcher is implemented as an overlay from the primary screen. When
    // the launcher is no longer in the foreground, it becomes invisible.
    // Exceptions are windows/message boxes that are opened in the context of
    // the launcher.
    
    // Too low screen resolution
    // ----
    // If the screen resolution is too low, an error message is displayed as an
    // overlay instead of the launcher.
    
    // Changes in user settings, keyboard layout or screen resolution
    // ----
    // In both cases, the launcher becomes invisible so that it can be redrawn
    // by pressing the host key again.
    
    internal partial class Control : Form
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        
        private const int HOTKEY_ID = 0x0;
        private const int WM_HOTKEY = 0x0312;

        private readonly MetaTile[] _metaTiles;
        private readonly MetaTileGrid _metaTileGrid;
        private readonly MetaTileScreen _metaTileScreen;
        
        private readonly Settings _settings;
        
        private int _cursor = -1;
        
        private bool _inputEventLock;
        private bool _visible;

        internal Control(Settings settings, bool visible = true)
        {
            _settings = settings;
            _visible = visible;

            FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.PrimaryScreen.Bounds;
            WindowState = FormWindowState.Maximized;

            #if DEBUG
            TopMost = false;
            #endif

            InitializeComponent();
            RegisterHotKey();
            
            Visible = false;
            Opacity = 0;

            Message.Font = new Font(SystemFonts.DefaultFont.FontFamily, 24, FontStyle.Regular);
            Message.ForeColor = ColorTranslator.FromHtml(_settings.ForegroundColor);
                
            BackColor = ColorTranslator.FromHtml(_settings.BackgroundColor);
            
            _metaTileGrid = MetaTileGrid.Create(_settings);
                        
            // The index for the configuration starts user-friendly with 1, but
            // internally it is technically started with 0. Therefore the index
            // in the configuration is different!
            _metaTiles = new MetaTile[_metaTileGrid.Count];

            var screen = Screen.FromControl(this);
            for (var index = 0; index < _metaTileGrid.Count; index++)
                _metaTiles[index] = MetaTile.Create(screen, _settings, new Settings.Tile() {Index = index +1});
            
            foreach (var tile in _settings.Tiles)
                if (tile.Index <= _metaTileGrid.Count
                        && tile.Index > 0)
                    _metaTiles[tile.Index - 1] = MetaTile.Create(screen, _settings, tile);

            _metaTileScreen = MetaTileScreen.Create(screen, _settings, _metaTiles);
            
            KeyDown += OnKeyDown;
            Load += OnLoad;
            LostFocus += OnLostFocus;
            MouseClick += OnMouseClick;

            SystemEvents.UserPreferenceChanging += (sender, eventArgs) => Visible = false;
            SystemEvents.DisplaySettingsChanged += (sender, eventArgs) => Visible = false;
        }

        private void RegisterHotKey()
        {
            try
            {
                var matchGroups = new Regex(@"^\s*(\d+)\s*\+\s*(\d+)\s*$").Matches(_settings.HotKey)[0].Groups;
                if (!RegisterHotKey(Handle, HOTKEY_ID, Int32.Parse(matchGroups[1].Value) , Int32.Parse(matchGroups[2].Value)))
                    throw new Exception();
            }
            catch (Exception)
            {
                MessageBox.Show("The settings do not contain a usable hot key."
                        + $"{Environment.NewLine}Please check the node /settings/hotKey in settings.xml file.",
                    "Virtual Environment Launcher", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                Environment.Exit(0);  
            }

            Closing += (sender, eventArgs) => UnregisterHotKey(Handle, HOTKEY_ID);
        }

        private void SelectMetaTile(MetaTile metaTile)
        {
            if (metaTile == null)
                return;
            _metaTileScreen.Select(CreateGraphics(), metaTile);
            _cursor = Array.IndexOf(_metaTiles, metaTile);
        }

        private void UseMetaTile(MetaTile metaTile)
        {
            if (Message.Visible)
                return;
            SelectMetaTile(metaTile);
            if (metaTile == null
                    || String.IsNullOrWhiteSpace(metaTile.Settings?.Destination))
                return;

            if (metaTile.Settings.Destination.Trim().ToLower().Equals("exit"))
                Environment.Exit(0);
            
            // There are always situations when opening programs can become a
            // problem. Even in these cases, the interface should not block.
            
            new Thread(delegate()
            {
                try 
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        WorkingDirectory = metaTile.Settings.WorkingDirectory,
                        FileName = metaTile.Settings.Destination,
                        Arguments = String.Join(" ", metaTile.Settings.Arguments ?? "")
                    });
                }
                catch (Exception exception)
                {
                    MessageBox.Show(($"Error opening file: {metaTile.Settings.Destination}"
                                     + $"{Environment.NewLine}{exception.Message}"
                                     + $"{Environment.NewLine}{exception.InnerException?.Message ?? ""}").Trim(),
                        "Virtual Environment Launcher", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }
            
                Visible = false;
                
            }).Start();
        }
        
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WM_HOTKEY
                    && message.WParam.ToInt32() == HOTKEY_ID)
                Visible = !Visible;
        }

        private void OnLoad(object sender, EventArgs eventArgs)
        {
            // Prevents possible flickering effects when drawing from the
            // background image for the first time.            
            DoubleBuffered = true;
            Thread.Sleep(25);
            Opacity = Math.Min(Math.Max(_settings.Opacity, 0), 100) /100d;
            Visible = _visible;
        }
        
        private static Process GetForegroundProcess()
        {
            uint processID = 0;
            GetWindowThreadProcessId(GetForegroundWindow(), out processID);
            return Process.GetProcessById(Convert.ToInt32(processID));
        }
        
        private void OnLostFocus(object sender, EventArgs eventArgs)
        {
            // In case of an error message when opening a tile, the Launcher
            // should continue to be displayed. If other programs get the
            // focus, the Launcher should be invisible.
            var process = Process.GetCurrentProcess();
            if (!process.Id.Equals(GetForegroundProcess()?.Id))
                Visible = false;
        }

        protected override bool ProcessKeyMessage(ref Message message)
        {
            if (message.Msg == 0x102
                    || message.Msg == 0x106)
                UseMetaTile(_metaTileScreen.Locate(Char.ToString((char)message.WParam)));
            return base.ProcessKeyMessage(ref message);
        }
        
        private void OnKeyDown(object sender, KeyEventArgs keyEventArgs)
        {
            if (Message.Visible)
                return;
            
            if (_cursor < 0
                    && (new List<Keys> {Keys.Left, Keys.Back, Keys.Up}).Contains(keyEventArgs.KeyCode))
                _cursor = 0;
            if (_cursor < 0
                    && (new List<Keys> {Keys.Right, Keys.Tab, Keys.Down}).Contains(keyEventArgs.KeyCode))
                _cursor = _metaTileGrid.Count;
            
            // When the key is held down, the input signals are very fast and
            // the cursor is barely visible. Therefore, the signal processing
            // is artificially slowed down. When the lock is active, new input
            // signals are ignored, which concerns only the navigation buttons

            var navigationKeys = (new List<Keys> {Keys.Left, Keys.Back, Keys.Right, Keys.Tab, Keys.Up, Keys.Down}); 
            if (navigationKeys.Contains(keyEventArgs.KeyCode)
                    && _inputEventLock)
                return;
            if (navigationKeys.Contains(keyEventArgs.KeyCode))
                _inputEventLock = true;
            
            // Key combinations with Shift invert the key functions for
            // navigation. Escape, Enter and Space are excluded from this.

            switch (keyEventArgs.KeyCode)
            {
                case (Keys.Escape):
                    Visible = false;
                    break;
                case Keys.Left when !keyEventArgs.Shift:
                case Keys.Back when !keyEventArgs.Shift:
                case Keys.Right when keyEventArgs.Shift:
                case Keys.Tab when keyEventArgs.Shift:
                    if (_cursor <= 0)
                        _cursor = _metaTileGrid.Count;
                    _cursor--;
                    break;
                case Keys.Right when !keyEventArgs.Shift:
                case Keys.Tab when !keyEventArgs.Shift:
                case Keys.Left when keyEventArgs.Shift:
                case Keys.Back when keyEventArgs.Shift:
                    if (_cursor + 1 >= _metaTileGrid.Count)
                        _cursor = -1;
                    _cursor++;
                    break;
                case Keys.Up when !keyEventArgs.Shift:
                case Keys.Down when keyEventArgs.Shift:
                    if (_cursor < _metaTileGrid.Columns
                            && _cursor > 0)
                        _cursor += _metaTileGrid.Count - 1;
                    _cursor -= _metaTileGrid.Columns;
                    if (_cursor < 0)
                        _cursor = _metaTileGrid.Count - 1;
                    break;
                case Keys.Down when !keyEventArgs.Shift:
                case Keys.Up when keyEventArgs.Shift:
                    if (_cursor >= _metaTileGrid.Count -_metaTileGrid.Columns
                            && _cursor < _metaTileGrid.Count - 1)
                        _cursor = (_cursor - _metaTileGrid.Count) + 1;
                    _cursor += _metaTileGrid.Columns;
                    if (_cursor >= _metaTileGrid.Count)
                        _cursor = 0;
                    break;
                case Keys.Enter:
                case Keys.Space:
                    if (_cursor >= 0)
                        UseMetaTile(_metaTiles[_cursor]);
                    break;
            }

            if (_cursor >= 0)
                SelectMetaTile(_metaTiles[_cursor]);
            
            if (!navigationKeys.Contains(keyEventArgs.KeyCode)
                    || !_inputEventLock)
                return;
            Thread.Sleep(50);
            _inputEventLock = false;
        }

        private void OnMouseClick(object sender, MouseEventArgs mouseEventArgs)
        {
            if (Message.Visible)
                return;
            var location = new Point(mouseEventArgs.X, mouseEventArgs.Y);
            var metaTile = _metaTileScreen.Locate(location);
            SelectMetaTile(metaTile);
            if ((mouseEventArgs.Button & MouseButtons.Left) == 0
                    || metaTile == null
                    || metaTile.Settings == null
                    || String.IsNullOrWhiteSpace(metaTile.Settings.Destination))
                return;
            UseMetaTile(metaTile);
        }

        protected override void OnPaintBackground(PaintEventArgs eventArgs)
        {
            base.OnPaintBackground(eventArgs);
            Message.Text = "";
            if (Screen.FromControl(this).Bounds.Width < _metaTileGrid.Width + _metaTileGrid.Gap 
                    || Screen.FromControl(this).Bounds.Height < _metaTileGrid.Height + _metaTileGrid.Gap)
                Message.Text = "The resolution is too low to show the tiles.";
            else _metaTileScreen.Draw(eventArgs.Graphics);
            Message.Visible = !String.IsNullOrWhiteSpace(Message.Text);
        }
    }
}