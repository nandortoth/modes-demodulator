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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RtlSdrManager;
using RtlSdrManager.Types;
using ModeSDemodulator.Types;

namespace ModeSDemodulator.Demo
{
    /// <summary>
    /// Demo for ModeSDecoder.
    /// In this demo:
    ///   - Demodulate I/Q samples to Mode S frames
    ///   - Display the Mode S raw frames
    /// </summary>
    /// <inheritdoc />
    public class Demo : IDisposable
    {
        /// <summary>
        /// Device Manager for RTL-SDR device.
        /// </summary>
        private readonly RtlSdrDeviceManager _deviceManager;

        /// <summary>
        /// Buffer to record ICAOs.
        /// </summary>
        private readonly ConcurrentDictionary<uint, Dictionary<string, object>> _icaoBuffer;

        /// <summary>
        /// Buffer to statistic data.
        /// </summary>
        private readonly ConcurrentDictionary<string, uint> _statisticBuffer;
        
        /// <summary>
        /// Buffer to Downlink Format statistic data.
        /// </summary>
        private readonly ConcurrentDictionary<int, uint> _statisticDFBuffer;

        /// <summary>
        /// Timeout for the ICAOs
        /// </summary>
        private readonly int _icaoTimeOut;

        /// <summary>
        /// Task for printing the data
        /// </summary>
        private Task _displayTask;

        /// <summary>
        /// Cancellation token.
        /// </summary>
        private CancellationTokenSource _cts;

        /// <summary>
        /// Field for IDisposable.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Timer to help maintain the ICAO lists.
        /// </summary>
        private readonly Timer _icaoListMaintenanceTimer;

        /// <summary>
        /// Mode S demodulator.
        /// </summary>
        private readonly ModeSDemodulator _demodulator;

        public Demo()
        {
            // Initialize the Manager instance.
            _deviceManager = RtlSdrDeviceManager.Instance;

            // Open a managed device and set some parameters.
            _deviceManager.OpenManagedDevice(0, "my-rtl-sdr");
            _deviceManager["my-rtl-sdr"].CenterFrequency = new Frequency {MHz = 1090};
            _deviceManager["my-rtl-sdr"].SampleRate = new Frequency {MHz = 2};
            _deviceManager["my-rtl-sdr"].TunerGainMode = TunerGainModes.AGC;
            _deviceManager["my-rtl-sdr"].FrequencyCorrection = 52;
            _deviceManager["my-rtl-sdr"].MaxAsyncBufferSize = 512 * 1024;
            _deviceManager["my-rtl-sdr"].DropSamplesOnFullBuffer = true;
            _deviceManager["my-rtl-sdr"].ResetDeviceBuffer();

            // Initialize the Mode S demodulator.
            _demodulator = new ModeSDemodulator()
            {
                ICAOConfidenceLevel = ICAOConfidenceLevels.Medium,
                ICAOTimeOut = 180
            };

            // Subscribe on the frame available event.
            _demodulator.ModeSFrameAvailable += (sender, args) => { UpdateICAOBufferAndStatistic(args.RawFrame); };
            
            // Initialize ICAO timeout.
            _icaoTimeOut = 60;

            // Initialize the ICAO maintainer function.
            _icaoListMaintenanceTimer = new Timer(RemoveICAOWhenTimeout, null, 10000, 10000);

            // Initialize ICAO buffer.
            _icaoBuffer = new ConcurrentDictionary<uint, Dictionary<string, object>>();

            // Initialize statistic buffer.
            _statisticBuffer = new ConcurrentDictionary<string, uint>
            {
                ["seenICAOs"] = 0,
                ["receivedMessages"] = 0,
            };

            // Initialize statistic buffer: fill downlink formats with zero.
            _statisticDFBuffer = new ConcurrentDictionary<int, uint>();
            foreach (var df in Enum.GetValues(typeof(ModeSDownlinkFormats)).Cast<int>().ToList())
            {
                _statisticDFBuffer[df] = 0;
            }
        }

        /// <summary>
        /// Start reading data from the RTL-SDR device and display the Mode S messages.
        /// </summary>
        public void Start()
        {
            // Start asynchronous sample reading.
            _deviceManager["my-rtl-sdr"].StartReadSamplesAsync();
            
            // Start demodulation.
            _demodulator.StartDemodulateSamplesAsync(_deviceManager["my-rtl-sdr"].AsyncBuffer);

            // Initialize cancellation token source and get the token.
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Start display thread.
            _displayTask = Task.Factory.StartNew(() =>
            {
                // Save the start time
                var startTime = DateTime.UtcNow;
                
                // Initialize the "spinner".
                var spinnerChars = new[] {'/', '-', '\\', '|'};
                var spinnerPointer = 0;

                // Timeout chars
                var timeoutChars = new[] {'▁', '▃', '▄', '▅', '▆', '▇'};

                // Initialize table header.
                // ICAO, Message Counter, Downlink Format (Actual / Previous), Last Seen, Raw Frame.
                Console.WriteLine("+--------+-------+---------+------------------+----------------------------+---+");
                Console.WriteLine("| ICAO   | Msgs. | DF      | Last Seen    T/O | Last Raw Frame             |   |");
                Console.WriteLine("+--------+-------+---------+------------------+----------------------------+---+");

                // Store spinner's position.
                const int spinnerTop = 1;
                const int spinnerLeft = 77;

                // Store window's properties.
                var windowHeight = Console.WindowHeight - 7;
                const int windowLeft = 0;
                const int windowTop = 3;
                
                // Store buffer info's position.
                var bufferInfoLeft = 10;
                var bufferInfoTop = Console.WindowHeight - 3;

                // Display the window.
                for (var i = 0; i < windowHeight; i++)
                {
                    Console.WriteLine($"| {" ",71} |");
                }

                // Display the footer.
                Console.WriteLine("+--------+-------+---------+------------------+--------------------------------+");
                Console.WriteLine("| Buffer:                                                                      |");
                Console.WriteLine("+------------------------------------------------------------------------------+");

                // Save cursor current top position.
                var windowBottom = Console.CursorTop;

                // Define infinitive loop to read data from ICAO buffer and display it.
                while (!token.IsCancellationRequested)
                {
                    // Update the spinner.
                    Console.SetCursorPosition(spinnerLeft, spinnerTop);
                    Console.Write(spinnerChars[spinnerPointer]);
                    spinnerPointer = (spinnerPointer + 1) % spinnerChars.Length;

                    // Set the cursor for the window.
                    Console.SetCursorPosition(windowLeft, windowTop);

                    // Local variables to calculate how many rows must be printed.
                    int icaoRowNumber;
                    int emptyRowNumber;
                    bool moreICAOMessage;

                    // If there are more ICAOs in the buffer than the available rows:
                    //   - Print one less ICAOs than the window's height, because "..." will be shown.
                    //   - Empty rows are not necessary.
                    //   - More ICAOs available ("...") will be shown.
                    if (_icaoBuffer.Count > windowHeight)
                    {
                        icaoRowNumber = windowHeight - 1;
                        emptyRowNumber = 0;
                        moreICAOMessage = true;
                    }

                    // If there less ICAOs in the buffer than the available rows:
                    //   - Print all the ICAOs.
                    //   - Print empty rows to fill the available space.
                    //   - More ICAOs available ("...") will not be shown.
                    else
                    {
                        icaoRowNumber = _icaoBuffer.Count;
                        emptyRowNumber = windowHeight - icaoRowNumber;
                        moreICAOMessage = false;
                    }

                    // Print ICAOs.
                    var icaosToPrint = _icaoBuffer.Take(icaoRowNumber).ToList();
                    foreach (var icao in icaosToPrint)
                    {
                        // Classify timeout value and create a timeout bar.
                        var timeout = _icaoTimeOut - (DateTime.UtcNow - (DateTime) icao.Value["seen"]).TotalSeconds;
                        var timeoutClass = (int) Math.Ceiling(
                                               timeout / ((double) _icaoTimeOut / timeoutChars.Length)) - 1;
                        timeoutClass = timeoutClass < 0 ? 0 : timeoutClass;
                        var timeoutBar = timeoutChars[timeoutClass];

                        // Print everything.
                        Console.WriteLine(
                            $"| {icao.Key:X6} " +
                            $"| {(uint) icao.Value["messages"],5} " +
                            $"| {(int) icao.Value["df"],-2} / {(int) icao.Value["ldf"],2} " +
                            $"| {(DateTime) icao.Value["seen"]:hh:mm:ss tt} {timeoutBar,4} " +
                            $"| {(string) icao.Value["raw"],-30} |");
                    }

                    // Print the empty rows.
                    for (var i = 0; i < emptyRowNumber; i++)
                    {
                        Console.WriteLine($"| {" ",6} | {" ",5} | {" ",7} | {" ",16} | {" ",30} |");
                    }

                    // Print the "..." characters.
                    if (moreICAOMessage)
                    {
                        Console.WriteLine($"| ...    | {" ",5} | {" ",7} | {" ",16} | {" ",30} |");
                    }
                    
                    // Update buffer info.
                    Console.SetCursorPosition(bufferInfoLeft, bufferInfoTop);
                    var bufferInfo =
                        $"{_deviceManager["my-rtl-sdr"].AsyncBuffer.Count} / " +
                        $"{_deviceManager["my-rtl-sdr"].MaxAsyncBufferSize} " +
                        $"({_deviceManager["my-rtl-sdr"].DroppedSamplesCount})";
                    Console.Write($"{bufferInfo, -68}");

                    // Set cursor position to the end.
                    Console.SetCursorPosition(0, windowBottom);

                    // Wait 1.0 second.
                    Thread.Sleep(1000);
                }
                
                // Print statistic, when displaying is stopped.
                Console.WriteLine();
                Console.WriteLine($"Seen ICAOs: {_statisticBuffer["seenICAOs"]}");
                Console.WriteLine($"Received messages: {_statisticBuffer["receivedMessages"]}");
                
                // Select the Downlink Formats which are not equal to invalid.
                Console.Write("Received messages by Downlink Formats:");
                var messagesByDFStatistic = _statisticDFBuffer
                    .Where(df => df.Key != (int) ModeSDownlinkFormats.Invalid).ToList();
                
                // Display the statistic of Downlink formats.
                for (var i = 0; i < messagesByDFStatistic.Count; i++)
                {
                    if (i % 2 == 0)
                    {
                        Console.Write("\n  ");
                    }
                    Console.Write($"[DF{messagesByDFStatistic[i].Key, -2}]: {messagesByDFStatistic[i].Value, -8}  ");  
                }
                
                // Display the running time.
                Console.WriteLine();
                Console.WriteLine($"Running time: {DateTime.UtcNow - startTime:g}");
            }, token);
        }

        /// <summary>
        /// Stop reading data from the RTL-SDR device.
        /// </summary>
        public void Stop()
        {
            // Set cancellation token.
            _cts.Cancel();

            // Wait for finishing task.
            _displayTask.Wait();
            
            // Stop the reading of the samples.
            _deviceManager["my-rtl-sdr"].StopReadSamplesAsync();   
            
            // Stop demodulation
            _demodulator.StopDemodulateSamplesAsync();
        }

        /// <summary>
        /// Update ICAO buffer and statistic by the Mode S frame.
        /// </summary>
        /// <param name="rawFrame">Mode S frame.</param>
        private void UpdateICAOBufferAndStatistic(ModeSRawFrame rawFrame)
        {
            // Update the ICAO's properties, if the ICAO is already in the buffer.
            if (_icaoBuffer.ContainsKey(rawFrame.ICAO))
            {
                _icaoBuffer[rawFrame.ICAO]["ldf"] = (int) _icaoBuffer[rawFrame.ICAO]["df"];
                _icaoBuffer[rawFrame.ICAO]["df"] = (int) rawFrame.DownlinkFormat;
                _icaoBuffer[rawFrame.ICAO]["raw"] = rawFrame.ToString();
                _icaoBuffer[rawFrame.ICAO]["messages"] = (uint) _icaoBuffer[rawFrame.ICAO]["messages"] + 1;
                _icaoBuffer[rawFrame.ICAO]["seen"] = DateTime.UtcNow;
            }
            // Add the ICAO to the buffer, if the ICAO is not in the buffer yet.
            else
            {
                _icaoBuffer[rawFrame.ICAO] = new Dictionary<string, object>
                {
                    {"ldf", 0},
                    {"df", (int) rawFrame.DownlinkFormat},
                    {"raw", rawFrame.ToString()},
                    {"messages", (uint) 1},
                    {"seen", DateTime.UtcNow}
                };
                _statisticBuffer["seenICAOs"] += 1;
            }

            // Update Downlink Format statistics.
            _statisticDFBuffer[(int) rawFrame.DownlinkFormat] += 1;

            // Update Downlink Format statistics.
            _statisticBuffer["receivedMessages"] += 1;
        }

        /// <summary>
        /// Remove ICAOs from the buffer, if the last seen value reached the timeout.
        /// </summary>
        /// <param name="status"></param>
        private void RemoveICAOWhenTimeout(object status)
        {
            // Select the candidate ICAOs, which reached their timeout.
            var removableICAOList = _icaoBuffer
                .Where(icao => (DateTime.UtcNow - (DateTime) icao.Value["seen"]).TotalSeconds > _icaoTimeOut)
                .Select(icao => icao.Key);

            // Remove them from the list.
            foreach (var icao in removableICAOList)
            {
                _icaoBuffer.TryRemove(icao, out _);
            }
        }

        /// <summary>
        /// Implement Dispose pattern callable by consumers.
        /// </summary>
        /// <inheritdoc />
        public void Dispose()
        {
            // Check to see if Dispose has already been called.
            if (_disposed)
            {
                return;
            }

            // Release managed device.
            _deviceManager.CloseAllManagedDevice();

            // Stop the timer.
            _icaoListMaintenanceTimer.Dispose();

            // Set disposed to true.
            _disposed = true;

            // Sign to GC, that the object can be dropped.
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Implement the destructor.
        /// </summary>
        ~Demo()
        {
            Dispose();
        }
    }
}