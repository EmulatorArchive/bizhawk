﻿using System;
using System.IO;

using BizHawk.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Common.IEmulatorExtensions;

namespace BizHawk.Client.Common
{
	public class Rewinder
	{
		private StreamBlobDatabase _rewindBuffer;
		private RewindThreader _rewindThread;
		private byte[] _lastState;
		private bool _rewindImpossible;
		private int _rewindFrequency = 1;
		private bool _rewindDeltaEnable;
		private byte[] _rewindFellationBuf;
		private byte[] _tempBuf = new byte[0];

		public Rewinder()
		{
			RewindActive = true;
		}

		public Action<string> MessageCallback { get; set; }
		public bool RewindActive { get; set; }

		// TODO: make RewindBuf never be null
		public float FullnessRatio
		{
			get { return _rewindBuffer.FullnessRatio; }
		}
		
		public int Count
		{
			get { return _rewindBuffer != null ? _rewindBuffer.Count : 0; }
		}
		
		public long Size 
		{
			get { return _rewindBuffer != null ? _rewindBuffer.Size : 0; }
		}
		
		public int BufferCount
		{
			get { return _rewindBuffer != null ? _rewindBuffer.Count : 0; }
		}

		public bool HasBuffer
		{
			get { return _rewindBuffer != null; }
		}

		public int RewindFrequency
		{
			get { return _rewindFrequency; }
		}

		// TOOD: this should not be parameterless?! It is only possible due to passing a static context in
		public void CaptureRewindState()
		{
			if (Global.Emulator.HasSavestates())
			{
				if (_rewindImpossible)
				{
					return;
				}

				if (_lastState == null)
				{
					DoRewindSettings();
				}

				// log a frame
				if (_lastState != null && Global.Emulator.Frame % _rewindFrequency == 0)
				{
					_rewindThread.Capture(Global.Emulator.AsStatable().SaveStateBinary());
				}
			}
		}

		public void DoRewindSettings()
		{
			if (Global.Emulator.HasSavestates())
			{
				// This is the first frame. Capture the state, and put it in LastState for future deltas to be compared against.
				_lastState = (byte[])Global.Emulator.AsStatable().SaveStateBinary().Clone();

				int state_size;
				if (_lastState.Length >= Global.Config.Rewind_LargeStateSize)
				{
					SetRewindParams(Global.Config.RewindEnabledLarge, Global.Config.RewindFrequencyLarge);
					state_size = 3;
				}
				else if (_lastState.Length >= Global.Config.Rewind_MediumStateSize)
				{
					SetRewindParams(Global.Config.RewindEnabledMedium, Global.Config.RewindFrequencyMedium);
					state_size = 2;
				}
				else
				{
					SetRewindParams(Global.Config.RewindEnabledSmall, Global.Config.RewindFrequencySmall);
					state_size = 1;
				}

				var rewind_enabled = false;
				if (state_size == 1)
				{
					rewind_enabled = Global.Config.RewindEnabledSmall;
				}
				else if (state_size == 2)
				{
					rewind_enabled = Global.Config.RewindEnabledMedium;
				}
				else if (state_size == 3)
				{
					rewind_enabled = Global.Config.RewindEnabledLarge;
				}

				_rewindDeltaEnable = Global.Config.Rewind_UseDelta;

				if (rewind_enabled)
				{
					var cap = Global.Config.Rewind_BufferSize * (long)1024 * 1024;

					if (_rewindBuffer != null)
					{
						_rewindBuffer.Dispose();
					}

					_rewindBuffer = new StreamBlobDatabase(Global.Config.Rewind_OnDisk, cap, BufferManage);

					if (_rewindThread != null)
					{
						_rewindThread.Dispose();
					}

					_rewindThread = new RewindThreader(this, Global.Config.Rewind_IsThreaded);
				}
			}
		}

		public void Rewind(int frames)
		{
			if (Global.Emulator.HasSavestates())
			{
				_rewindThread.Rewind(frames);
			}
		}

		// TODO remove me
		public void _RunRewind(int frames)
		{
			for (int i = 0; i < frames; i++)
			{
				if (_rewindBuffer.Count == 0 || (Global.MovieSession.Movie.IsActive && Global.MovieSession.Movie.InputLogLength == 0))
				{
					return;
				}

				if (_lastState.Length < 0x10000)
				{
					Rewind64K();
				}
				else
				{
					RewindLarge();
				}
			}
		}

		// TODO: only run by RewindThreader, refactor
		public void RunCapture(byte[] coreSavestate)
		{
			if (_rewindDeltaEnable)
			{
				CaptureRewindStateDelta(coreSavestate);
			}
			else
			{
				CaptureRewindStateNonDelta(coreSavestate);
			}
		}

		public void ResetRewindBuffer()
		{
			if (_rewindBuffer != null)
			{
				_rewindBuffer.Clear();
			}

			_rewindImpossible = false;
			_lastState = null;
		}

		private void DoMessage(string message)
		{
			if (MessageCallback != null)
			{
				MessageCallback(message);
			}
		}

		private void SetRewindParams(bool enabled, int frequency)
		{
			if (RewindActive != enabled)
			{
				DoMessage("Rewind " + (enabled ? "Enabled" : "Disabled"));
			}

			if (_rewindFrequency != frequency && enabled)
			{
				DoMessage("Rewind frequency set to " + frequency);
			}

			RewindActive = enabled;
			_rewindFrequency = frequency;

			if (!RewindActive)
			{
				_lastState = null;
			}
		}

		private byte[] BufferManage(byte[] inbuf, long size, bool allocate)
		{
			if (allocate)
			{
				// if we have an appropriate buffer free, return it
				if (_rewindFellationBuf != null && _rewindFellationBuf.LongLength == size)
				{
					var ret = _rewindFellationBuf;
					_rewindFellationBuf = null;
					return ret;
				}

				// otherwise, allocate it
				return new byte[size];
			}
			
			_rewindFellationBuf = inbuf;
			return null;
		}

		private void CaptureRewindStateNonDelta(byte[] currentState)
		{
			long offset = _rewindBuffer.Enqueue(0, currentState.Length + 1);
			var stream = _rewindBuffer.Stream;
			stream.Position = offset;

			// write the header for a non-delta frame
			stream.WriteByte(1); // i.e. true
			stream.Write(currentState, 0, currentState.Length);
		}

		private unsafe void CaptureRewindStateDelta(byte[] currentState)
		{
			// in case the state sizes mismatch, capture a full state rather than trying to do anything clever
			if (currentState.Length != _lastState.Length)
			{
				CaptureRewindStateNonDelta(currentState);
				return;
			}

			int index = 0;
			int stateLength = Math.Min(currentState.Length, _lastState.Length);
			bool inChangeSequence = false;
			int changeSequenceStartOffset = 0;
			int lastChangeSequenceStartOffset = 0;

			if (_tempBuf.Length < stateLength)
			{
				_tempBuf = new byte[stateLength];
			}

			_tempBuf[index++] = 0; // Full state (false = delta)

			fixed (byte* pCurrentState = &currentState[0])
			fixed (byte* pLastState = &_lastState[0])
			for (int i = 0; i < stateLength; i++)
			{
				bool thisByteMatches = *(pCurrentState + i) == *(pLastState + i);

				if (inChangeSequence == false)
				{
					if (thisByteMatches)
					{
						continue;
					}

					inChangeSequence = true;
					changeSequenceStartOffset = i;
				}

				if (thisByteMatches || i == stateLength - 1)
				{
					const int maxHeaderSize = 10;
					int length = i - changeSequenceStartOffset + (thisByteMatches ? 0 : 1);

					if (index + length + maxHeaderSize >= stateLength)
					{
						// If the delta ends up being larger than the full state, capture the full state instead
						CaptureRewindStateNonDelta(currentState);
						return;
					}

					// Offset Delta
					VLInteger.WriteUnsigned((uint)(changeSequenceStartOffset - lastChangeSequenceStartOffset), _tempBuf, ref index);

					// Length
					VLInteger.WriteUnsigned((uint)length, _tempBuf, ref index);

					// Data
					Buffer.BlockCopy(_lastState, changeSequenceStartOffset, _tempBuf, index, length);
					index += length;

					inChangeSequence = false;
					lastChangeSequenceStartOffset = changeSequenceStartOffset;
				}
			}

			Buffer.BlockCopy(currentState, 0, _lastState, 0, _lastState.Length);

			_rewindBuffer.Push(new ArraySegment<byte>(_tempBuf, 0, index));
		}

		private void RewindLarge() 
		{
			RewindDelta(false); 
		}
		
		private void Rewind64K() 
		{
			RewindDelta(true); 
		}

		private void RewindDelta(bool isSmall)
		{
			if (Global.Emulator.HasSavestates())
			{
				var ms = _rewindBuffer.PopMemoryStream();
				var reader = new BinaryReader(ms);
				var fullstate = reader.ReadBoolean();
				if (fullstate)
				{
					Global.Emulator.AsStatable().LoadStateBinary(reader);
				}
				else
				{
					byte[] buf = ms.GetBuffer();
					var output = new MemoryStream(_lastState);
					int index = 1;
					int offset = 0;

					while (index < buf.Length)
					{
						int offsetDelta = (int)VLInteger.ReadUnsigned(buf, ref index);
						int length = (int)VLInteger.ReadUnsigned(buf, ref index);

						offset += offsetDelta;
						
						output.Position = offset;
						output.Write(buf, index, length);
						index += length;
					}

					reader.Close();
					output.Position = 0;
					Global.Emulator.AsStatable().LoadStateBinary(new BinaryReader(output));
				}
			}
		}
	}
}
