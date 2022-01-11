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
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
// License for the specific language governing permissions and limitations under
// the License.

using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;
    
// TODO: MessageBox does not show the correct icon in taskbar

namespace Seanox.Virtual.Environment.Launcher
{
    internal static class Program
    {
        private static Settings _settings;
        
        [STAThread]
        internal static void Main()
        {
            // XML based configuration
            // - based on the file name from launcher
            // - contains: Opacity, HotKey, Apps (Tiles)
            // - XML data is mapped to settings via serialization
            // - here there is no validation only indirectly it is checked
            //   during serialization whether the data types fit.
            
            var applicationPath = Assembly.GetExecutingAssembly().Location;
            var applicationConfigurationFile = Path.Combine(Path.GetDirectoryName(applicationPath),
                    Path.GetFileNameWithoutExtension(applicationPath) + ".xml");

            try
            {
                var serializer = new XmlSerializer(typeof(Settings));
                using (var reader = new StreamReader(applicationConfigurationFile))
                    _settings = (Settings) serializer.Deserialize(reader);
            }
            catch (FileNotFoundException)
            {
                MessageBox.Show("The settings file is missing:"
                                + $"{System.Environment.NewLine}{applicationConfigurationFile}",
                    "Virtual Environment Launcher", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            catch (Exception exception)
            {
                MessageBox.Show(("The settings file is incorrect:"
                        + $"{System.Environment.NewLine}{exception.Message}"
                        + $"{System.Environment.NewLine}{exception.InnerException?.Message ?? ""}").Trim(),
                    "Virtual Environment Launcher", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Worker(_settings));
        }
    }
}