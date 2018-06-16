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
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using ModeSDemodulator.Types;

namespace ModeSDemodulator
{
    /// <summary>
    /// This class demodulates I/Q data to Mode S frames.
    /// </summary>
    /// <inheritdoc />
    public partial class ModeSDemodulator : IDisposable
    {
        #region Constants

        /// <summary>
        /// Length of the long frame in bits.
        /// </summary>
        private const int LongFrameBitLength = 112;

        /// <summary>
        /// Length of the short frame in bits.
        /// </summary>
        private const int ShortFrameBitLength = 56;

        /// <summary>
        /// Length of preamble in bits.
        /// </summary>
        private const int PreambleBitLength = 16;

        /// <summary>
        /// Default magnitude correction factor.
        /// </summary>
        private const float CorrectionFactor = 20.0f;

        #endregion

        #region Fields

        /// <summary>
        /// Helper field since the Math.Sqrt function is expensive.
        /// </summary>
        private int[,] _magnitudeLookupTable;

        /// <summary>
        /// Field to store trusted ICAOs.
        /// </summary>
        private readonly ConcurrentDictionary<uint, DateTime> _trustedICAOList;

        /// <summary>
        /// Field to store candidate ICAOs.
        /// </summary>
        private readonly ConcurrentDictionary<uint, Dictionary<string, object>> _candidateICAOList;

        /// <summary>
        /// Buffer to store the Mode S frame.
        /// </summary>
        private readonly byte[] _frameBuffer;

        /// <summary>
        /// Buffer to store the candidate Mode S frame.
        /// </summary>
        private readonly int[] _candidateFrameBuffer;

        /// <summary>
        /// Pointer on the actual candidate frame's bit.
        /// </summary>
        private int _candidateFramePointer;

        /// <summary>
        /// Timer to help maintain the ICAO lists.
        /// </summary>
        private readonly Timer _icaoListMaintenanceTimer;

        /// <summary>
        /// Private field to implement IDispose interface.
        /// </summary>
        private bool _disposed;

        #endregion

        #region Properties, Events

        /// <summary>
        /// Confidence level of ICAOs.
        /// </summary>
        public ICAOConfidenceLevels ICAOConfidenceLevel { get; set; }

        /// <summary>
        /// Timeout of ICAOs in seconds.
        /// </summary>
        public double ICAOTimeOut { get; set; }

        /// <summary>
        /// Event, if new Mode S frame is available.
        /// </summary>
        public event EventHandler<ModeSFrameAvailableEventArgs> ModeSFrameAvailable;

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize ModeSMessageDetector instance.
        /// </summary>
        public ModeSDemodulator()
        {
            // Set the default confidence level.
            ICAOConfidenceLevel = ICAOConfidenceLevels.Medium;

            // Set the default timeout (3 minutes = 180 seconds).
            ICAOTimeOut = 180;

            // Initialize trusted ICAO's list.
            _trustedICAOList = new ConcurrentDictionary<uint, DateTime>();

            // Initialize the ICAO maintain function.
            _icaoListMaintenanceTimer = new Timer(RemoveICAOWhenTimeout, null, 10000, 10000);

            // Initialize candidate ICAO's list.
            _candidateICAOList = new ConcurrentDictionary<uint, Dictionary<string, object>>();

            // Initialize the frame buffer.
            _frameBuffer = new byte[LongFrameBitLength / 8];

            // Initialize the candidate frame buffer.
            _candidateFrameBuffer = new int[PreambleBitLength + LongFrameBitLength * 2];

            // Fill the magnitude table to avoid the cost of square root calculation many times.
            FillMagnitudeLookupTable();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Remove ICAOs from the trusted and candidate list, if their last seen time is higher than the ICAO timeout.
        /// </summary>
        /// <param name="status"></param>
        private void RemoveICAOWhenTimeout(object status)
        {
            // Select the trusted ICAOs, which reached their timeout.
            var removableTrustedICAOList = _trustedICAOList
                .Where(icao => (DateTime.UtcNow - icao.Value).TotalSeconds > ICAOTimeOut)
                .Select(icao => icao.Key);

            // Remove ICAOs from the trusted list.
            foreach (var icao in removableTrustedICAOList)
            {
                _trustedICAOList.TryRemove(icao, out _);
            }

            // Select the candidate ICAOs, which reached their timeout.
            var removableCandidateICAOList = _candidateICAOList
                .Where(icao => (DateTime.UtcNow - (DateTime) icao.Value["lastSeen"]).TotalSeconds > ICAOTimeOut)
                .Select(icao => icao.Key);

            // Remove ICAOs from the candidate list.
            foreach (var icao in removableCandidateICAOList)
            {
                _candidateICAOList.TryRemove(icao, out _);
            }
        }

        /// <summary>
        /// Check the validity of the preamble.
        /// </summary>
        /// <returns></returns>
        private bool IsPreambleValid()
        {
            // Get the current candidate frame pointer and frame buffer's length.
            var pointer = _candidateFramePointer;
            var length = _candidateFrameBuffer.Length;

            // Preample: 1, 0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0.
            // The relation must be 1, 0, 1, 0, 0, 0, 0, 1, 0, 1.
            if (!(_candidateFrameBuffer[(pointer + 0) % length] > _candidateFrameBuffer[(pointer + 1) % length] &&
                  _candidateFrameBuffer[(pointer + 1) % length] < _candidateFrameBuffer[(pointer + 2) % length] &&
                  _candidateFrameBuffer[(pointer + 2) % length] > _candidateFrameBuffer[(pointer + 3) % length] &&
                  _candidateFrameBuffer[(pointer + 3) % length] < _candidateFrameBuffer[(pointer + 0) % length] &&
                  _candidateFrameBuffer[(pointer + 4) % length] < _candidateFrameBuffer[(pointer + 0) % length] &&
                  _candidateFrameBuffer[(pointer + 5) % length] < _candidateFrameBuffer[(pointer + 0) % length] &&
                  _candidateFrameBuffer[(pointer + 6) % length] < _candidateFrameBuffer[(pointer + 0) % length] &&
                  _candidateFrameBuffer[(pointer + 7) % length] > _candidateFrameBuffer[(pointer + 8) % length] &&
                  _candidateFrameBuffer[(pointer + 8) % length] < _candidateFrameBuffer[(pointer + 9) % length] &&
                  _candidateFrameBuffer[(pointer + 9) % length] > _candidateFrameBuffer[(pointer + 6) % length]))
            {
                return false;
            }

            // Calculate the average of the spike's magnitude.
            var highAverage = (_candidateFrameBuffer[(pointer + 0) % length] +
                               _candidateFrameBuffer[(pointer + 2) % length] +
                               _candidateFrameBuffer[(pointer + 7) % length] +
                               _candidateFrameBuffer[(pointer + 9) % length]) / 6;

            // The samples between the spikes must be lower than the average of the high levels.
            if (_candidateFrameBuffer[(pointer + 4) % length] >= highAverage ||
                _candidateFrameBuffer[(pointer + 5) % length] >= highAverage)
            {
                return false;
            }

            // The samples at the end of the preamble must be lower than the average of the high levels.
            if (_candidateFrameBuffer[(pointer + 11) % length] >= highAverage ||
                _candidateFrameBuffer[(pointer + 12) % length] >= highAverage ||
                _candidateFrameBuffer[(pointer + 13) % length] >= highAverage ||
                _candidateFrameBuffer[(pointer + 14) % length] >= highAverage)
            {
                return false;
            }

            // The checks were successful, the preamble is valid.
            return true;
        }

        /// <summary>
        /// Process the data. Will be invoked after preamble validation. 
        /// </summary>
        private void ProcessSample()
        {
            // Reset the frame buffer.
            Array.Clear(_frameBuffer, 0, _frameBuffer.Length);

            // Set the initial helper variables.
            var frameBitLength = 0;
            var lastMagnitude = 0f;
            var lastAverageMagnitude = 0f;

            // Get the actual pointer and skips the preamble.
            var pointer = (_candidateFramePointer + PreambleBitLength) % _candidateFrameBuffer.Length;

            // Go through on the candidate frame, and creates a final one.
            for (var i = 0; i < LongFrameBitLength * 2; i++)
            {
                // Get the magnitude from the action pointer and steps forward.
                var magnitude = (float) _candidateFrameBuffer[pointer];
                pointer = (pointer + 1) % _candidateFrameBuffer.Length;

                // If the bit is even, does other tasks.
                if (i % 2 != 0)
                {
                    // Calculate the new average of magnitude.
                    var averageMagnitude = (magnitude + lastMagnitude) * 0.5f;

                    // Apply correction, if the last average is higher than 0.
                    if (lastAverageMagnitude > 0f)
                    {
                        // Calculate correction.
                        var correction = -CorrectionFactor * (averageMagnitude - lastAverageMagnitude) /
                                         averageMagnitude;

                        // If the correction is higher than 0, apply correction on the magnitude value,
                        // and calculate the new average.
                        if (correction > 0)
                        {
                            magnitude += correction;
                            averageMagnitude = (magnitude + lastMagnitude) * 0.5f;
                        }
                    }

                    // Save the average as "previous" for further usage.
                    lastAverageMagnitude = averageMagnitude;

                    // Decide about the value of the bit based on the magnitude.
                    var bit = lastMagnitude > magnitude ? 1 : 0;

                    // Get bit position from sample position.
                    var frameBitPosition = i / 2;

                    // Store the bit, if the bit's value is 1, 
                    // Remark: previously the frame buffer was filled with zeros.
                    if (bit == 1)
                    {
                        var index = frameBitPosition / 8;
                        var shift = 7 - (frameBitPosition % 8);
                        _frameBuffer[index] += (byte) (1 << shift);
                    }

                    // Decide about the downlink format (DF), if the frame's bit position is 7.
                    if (frameBitPosition == 7)
                    {
                        // Return, if the first byte is zero (the frame is not valid).
                        if (_frameBuffer[0] == 0)
                        {
                            return;
                        }

                        // Get the downlink format of the frame.
                        var df = ModeSHelper.GetDownlinkFormat(_frameBuffer);

                        // Assign the good length to the frame.
                        switch (df)
                        {
                            // Short DFs.
                            case ModeSDownlinkFormats.DF0:
                            case ModeSDownlinkFormats.DF4:
                            case ModeSDownlinkFormats.DF5:
                            case ModeSDownlinkFormats.DF11:
                                frameBitLength = ShortFrameBitLength;
                                break;

                            // Long DFs.
                            case ModeSDownlinkFormats.DF16:
                            case ModeSDownlinkFormats.DF17:
                            case ModeSDownlinkFormats.DF18:
                            case ModeSDownlinkFormats.DF20:
                            case ModeSDownlinkFormats.DF21:
                            case ModeSDownlinkFormats.DF24:
                                frameBitLength = LongFrameBitLength;
                                break;

                            // Any other number is not valid.
                            default:
                                return;
                        }
                    }

                    // Approach the final bit of the frame. 
                    if (frameBitLength > 0 &&
                        frameBitPosition == frameBitLength - 1)
                    {
                        // Get length of the frame in byte.
                        var frameByteLength = frameBitLength / 8;

                        // If the last three bits are 0, the frame is not valid.
                        if (_frameBuffer[frameByteLength - 1] == 0 &&
                            _frameBuffer[frameByteLength - 2] == 0 &&
                            _frameBuffer[frameByteLength - 3] == 0)
                        {
                            return;
                        }

                        // Create the final frame buffer with valid length.
                        var finalFrameBuffer = _frameBuffer.Take(frameByteLength).ToArray();

                        // Local variable to store ICAO of the frame.
                        uint icao;

                        // Check the parity.
                        var df = ModeSHelper.GetDownlinkFormat(finalFrameBuffer);
                        switch (df)
                        {
                            // Check the frames which have Parity/Interrogator.
                            case ModeSDownlinkFormats.DF11:
                            case ModeSDownlinkFormats.DF17:
                            case ModeSDownlinkFormats.DF18:
                                // The syndrome must be zero, this means that the frame is valid.
                                if (ModeSParity.Syndrome(finalFrameBuffer) != 0)
                                {
                                    return;
                                }

                                // Get the ICAO from the frame.
                                icao = ModeSHelper.GetICAOAddress(finalFrameBuffer);

                                // Break.
                                break;

                            // Check the frames which have Address/Parity.
                            case ModeSDownlinkFormats.DF0:
                            case ModeSDownlinkFormats.DF4:
                            case ModeSDownlinkFormats.DF5:
                            case ModeSDownlinkFormats.DF16:
                            case ModeSDownlinkFormats.DF20:
                            case ModeSDownlinkFormats.DF21:
                            case ModeSDownlinkFormats.DF24:
                                // Get the ICAO from the Address/Parity.
                                icao = ModeSHelper.GetICAOAddress(finalFrameBuffer);

                                // If this ICAO is in the trusted list, we assume that the frame is valid.
                                if (!_trustedICAOList.ContainsKey(icao))
                                {
                                    return;
                                }

                                // Break.
                                break;

                            // Invalid DF.
                            default:
                                return;
                        }

                        // Return, if the the ICAO is not trusted and the candidate's confidence is not enough.
                        if (!_trustedICAOList.ContainsKey(icao) &&
                            !GetCandidateICAOConfidence(icao))
                        {
                            UpdateCandidateICAOList(icao);
                            return;
                        }

                        // Update trused ICAO's "lastseen" value and remove the ICAO from the candidate list.
                        _trustedICAOList[icao] = DateTime.UtcNow;
                        _candidateICAOList.TryRemove(icao, out _);

                        // Raise the ModeSFrameAvailable event.
                        var args = new ModeSFrameAvailableEventArgs(
                            new ModeSRawFrame(finalFrameBuffer, icao, df));
                        OnFrameAvailable(args);

                        // Return.
                        return;
                    }
                }

                // Save the current magnitude as "previous" for the future.
                lastMagnitude = magnitude;
            }
        }

        /// <summary>
        /// Ensure that registered delegates receive the FrameAvailable event.
        /// </summary>
        /// <param name="e">Event argument.</param>
        private void OnFrameAvailable(ModeSFrameAvailableEventArgs e)
        {
            // If there are subscriber(s), raises event.
            ModeSFrameAvailable?.Invoke(this, e);
        }

        /// <summary>
        /// Update candidate frame's valid message statistic value.
        /// </summary>
        /// <param name="icao"></param>
        private void UpdateCandidateICAOList(uint icao)
        {
            // If the ICAO is already known, increase the message number.
            if (_candidateICAOList.ContainsKey(icao))
            {
                _candidateICAOList[icao]["lastSeen"] = DateTime.UtcNow;
                _candidateICAOList[icao]["validFrames"] = (int) _candidateICAOList[icao]["validFrames"] + 1;
            }
            // If the ICAO wasn't know, the message number will be 1.
            else
            {
                _candidateICAOList[icao] = new Dictionary<string, object>
                {
                    {"lastSeen", DateTime.UtcNow},
                    {"validFrames", 1}
                };
            }
        }

        /// <summary>
        /// Check candidate frame's "rank" which is the number of the valid messages.
        /// </summary>
        /// <param name="icao">ICAO.</param>
        /// <returns>Confidence of the ICAO.</returns>
        private bool GetCandidateICAOConfidence(uint icao)
        {
            // Get the current "rank".
            var rank = _candidateICAOList.ContainsKey(icao) ? (int) _candidateICAOList[icao]["validFrames"] : 0;

            // Compare "rank" to the expected confidence level.
            return rank >= (int) ICAOConfidenceLevel;
        }

        /// <summary>
        /// Fill the magnitude table for further lookup.
        /// </summary>
        private void FillMagnitudeLookupTable()
        {
            // Initialize the 256x256 table.
            _magnitudeLookupTable = new int[256, 256];

            // Original code came from:
            // https://github.com/MalcolmRobb/dump1090/blob/master/dump1090.c
            //
            // Each I and Q value varies from 0 to 255, which represents a range from -1 to +1. To get from the 
            // unsigned (0-255) range you therefore subtract 127 (or 128 or 127.5) from each I and Q, giving you 
            // a range from -127 to +128 (or -128 to +127, or -127.5 to +127.5)..
            //
            // To decode the AM signal, you need the magnitude of the waveform, which is given by sqrt((I^2)+(Q^2))
            // The most this could be is if I&Q are both 128 (or 127 or 127.5), so you could end up with a magnitude 
            // of 181.019 (or 179.605, or 180.312)
            //
            // However, in reality the magnitude of the signal should never exceed the range -1 to +1, because the 
            // values are I = rCos(w) and Q = rSin(w). Therefore the integer computed magnitude should (can?) never 
            // exceed 128 (or 127, or 127.5 or whatever)
            //
            // If we scale up the results so that they range from 0 to 65535 (16 bits) then we need to multiply 
            // by 511.99, (or 516.02 or 514). Antirez's original code multiplies by 360, presumably because he's 
            // assuming the maximim calculated amplitude is 181.019, and (181.019 * 360) = 65166.
            //
            // So lets see if we can improve things by subtracting 127.5, Well in integer arithmatic we can't
            // subtract half, so, we'll double everything up and subtract one, and then compensate for the doubling 
            // in the multiplier at the end.
            //
            // If we do this we can never have I or Q equal to 0 - they can only be as small as +/- 1.
            // This gives us a minimum magnitude of root 2 (0.707), so the dynamic range becomes (1.414-255). This 
            // also affects our scaling value, which is now 65535/(255 - 1.414), or 258.433254
            //
            // The sums then become mag = 258.433254 * (sqrt((I*2-255)^2 + (Q*2-255)^2) - 1.414)
            //                   or mag = (258.433254 * sqrt((I*2-255)^2 + (Q*2-255)^2)) - 365.4798
            //
            // We also need to clip mag just in case any rogue I/Q values somehow do have a magnitude greater than 255.
            for (var i = 0; i <= 255; i++)
            {
                for (var q = 0; q <= 255; q++)
                {
                    // Calculate I and Q value.
                    var magI = i * 2 - 255;
                    var magQ = q * 2 - 255;

                    // Calculate the magnitude.
                    var magnitude =
                        Math.Round((Math.Sqrt(Math.Pow(magI, 2) + Math.Pow(magQ, 2)) * 258.433254) - 365.4798);

                    // Rouge I/Q.
                    if (magnitude > 65535)
                    {
                        magnitude = 65535;
                    }

                    // Fill the magnitude table.
                    _magnitudeLookupTable[i, q] = (int) magnitude;
                }
            }
        }

        #endregion

        #region Implement IDisposable

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

            // Release ICAO timer.
            _icaoListMaintenanceTimer.Dispose();

            // Set disposed to true.
            _disposed = true;

            // Sign to GC, that the object can be drop.
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Implement the destructor.
        /// </summary>
        ~ModeSDemodulator()
        {
            Dispose();
        }

        #endregion
    }
}