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

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ModeSDemodulator.Exceptions;
using RtlSdrManager.Types;

namespace ModeSDemodulator
{
    /// <summary>
    /// This class demodulates I/Q data to Mode S frames.
    /// </summary>
    /// <inheritdoc />
    public partial class ModeSDemodulator
    {
        /// <summary>
        /// Worker task for async sample demodulation.
        /// </summary>
        private Task _asyncWorker;

        /// <summary>
        /// Cancellation token source for async sample demodulation.
        /// </summary>
        private CancellationTokenSource _asyncCts;

        /// <summary>
        /// Demodulate the I/Q samples, and create Mode S frames directly from a buffer.
        /// </summary>
        /// <param name="samplesBuffer"></param>
        public void StartDemodulateSamplesAsync(ConcurrentQueue<IQData> samplesBuffer)
        {
            // Check the worker thread.
            if (_asyncWorker != null)
            {
                throw new ModeSLibraryExecutionException(
                    "Problem happened during asynchronious sample demodulation." +
                    "The worker thread is already started.");
            }

            // Initialize cancellation token source.
            _asyncCts = new CancellationTokenSource();
            var token = _asyncCts.Token;

            // Start new task to demodulate the samples from directly the buffer.
            _asyncWorker = Task.Factory.StartNew(() =>
            {
                // Read samples from the buffer, till cancellation request.
                while (!token.IsCancellationRequested)
                {
                    // Try to get an element from the buffer.
                    // In case lack of success, wait for 0.1 second.
                    if (!samplesBuffer.TryDequeue(out var sample))
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    // Calculate the magnitude.
                    var magnitude = _magnitudeLookupTable[sample.I, sample.Q];

                    // Set the candidate head to the current value.
                    _candidateFrameBuffer[_candidateFramePointer] = magnitude;

                    // Step the index.
                    _candidateFramePointer = (_candidateFramePointer + 1) % _candidateFrameBuffer.Length;

                    // Start the data processing, if the frame's preamble is valid. 
                    if (IsPreambleValid())
                    {
                        ProcessSample();
                    }
                }
            }, token);
        }

        /// <summary>
        /// Stop the async demodulation of I/Q samples.
        /// </summary>
        public void StopDemodulateSamplesAsync()
        {
            // Cancel the async worker.
            _asyncCts.Cancel();

            // Dispose the cancellation source.
            _asyncCts.Dispose();
        }
    }
}