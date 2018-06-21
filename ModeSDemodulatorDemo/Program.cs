// .NET Core library to demodulate Mode S frames
// Copyright (C) 2018 Nandor Toth <dev@nandortoth.eu>
//  
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see http://www.gnu.org/licenses.

using System;
using System.Globalization;
using System.Threading;
using RtlSdrManager.Exceptions;

namespace ModeSDemodulator.Demo
{
    /// <summary>
    /// Simple demo for ModeSDemodulator.
    /// </summary>
    public static class Program
    {
        public static void Main()
        {
            // Initialize Demo.
            try
            {
                // Set en-US culture info.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
                
                // Initialize the demo.
                var demo = new Demo();

                // Stop the demo when CTRL+C.
                Console.CancelKeyPress += (sender, args) =>
                {
                    demo.Stop();
                    demo.Dispose();
                };

                // Start the demo.
                demo.Start();
            }
            catch (DllNotFoundException)
            {
                // Rise an error message, if the library does not exist on the system.
                Console.WriteLine("The RTL-SDR library cannot be found. Please install it first.");
            }
            catch (RtlSdrLibraryExecutionException e)
            {
                // Rise an error message, if the there is no RTL-SDR device on the system.
                Console.WriteLine("The RTL-SDR device cannot be found.");
                Console.WriteLine(e);
            }
        }
    }
}