﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using BizHawk.Common;
using BizHawk.Emulation.Common;

namespace BizHawk.Client.Common
{
	public partial class Bk2Movie : IMovie
	{
		private readonly PlatformFrameRates _frameRates = new PlatformFrameRates();
		private bool _makeBackup = true;

		public Bk2Movie(string filename)
			: this()
		{
			Subtitles = new SubtitleList();
			Comments = new List<string>();

			Rerecords = 0;
			Filename = filename;
		}

		public Bk2Movie()
		{
			Filename = string.Empty;
			IsCountingRerecords = true;
			_mode = Moviemode.Inactive;
			_makeBackup = true;

			Header[HeaderKeys.MOVIEVERSION] = "BizHawk v2.0.0";
		}

		public string Filename { get; set; }

		public virtual string PreferredExtension { get { return "bk2"; } }

		public bool Changes { get; private set; }
		public bool IsCountingRerecords { get; set; }

		public double FrameCount
		{
			get
			{
				if (LoopOffset.HasValue)
				{
					return double.PositiveInfinity;
				}

				return _log.Length;
			}
		}

		public double Fps
		{
			get
			{
				var system = Header[HeaderKeys.PLATFORM];
				var pal = Header.ContainsKey(HeaderKeys.PAL) &&
					Header[HeaderKeys.PAL] == "1";

				return _frameRates[system, pal];
			}
		}

		public TimeSpan Time
		{
			get
			{
				var dblseconds = GetSeconds(_log.Length);
				var seconds = (int)(dblseconds % 60);
				var days = seconds / 86400;
				var hours = seconds / 3600;
				var minutes = (seconds / 60) % 60;
				var milliseconds = (int)((dblseconds - seconds) * 1000);
				return new TimeSpan(days, hours, minutes, seconds, milliseconds);
			}
		}

		public int InputLogLength
		{
			get { return _log.Length; }
		}

		#region Log Editing

		public void AppendFrame(IController source)
		{
			var mg = new MnemonicsGenerator();
			mg.SetSource(source);
			_log.AppendFrame(mg.GetControllersAsMnemonic());
			Changes = true;
		}

		public void RecordFrame(int frame, IController source)
		{
			if (Global.Config.VBAStyleMovieLoadState)
			{
				if (Global.Emulator.Frame < _log.Length)
				{
					_log.TruncateMovie(Global.Emulator.Frame);
				}
			}

			var mg = new MnemonicsGenerator();
			mg.SetSource(source);

			Changes = true;
			_log.SetFrameAt(frame, mg.GetControllersAsMnemonic());
		}

		public void Truncate(int frame)
		{
			_log.TruncateMovie(frame);
			Changes = true;
		}

		public string GetInput(int frame)
		{
			if (frame < FrameCount && frame >= 0)
			{

				int getframe;

				if (LoopOffset.HasValue)
				{
					if (frame < _log.Length)
					{
						getframe = frame;
					}
					else
					{
						getframe = ((frame - LoopOffset.Value) % (_log.Length - LoopOffset.Value)) + LoopOffset.Value;
					}
				}
				else
				{
					getframe = frame;
				}

				return _log[getframe];
			}

			Finish();
			return string.Empty;
		}

		#endregion

		private double GetSeconds(int frameCount)
		{
			double frames = frameCount;

			if (frames < 1)
			{
				return 0;
			}

			return frames / Fps;
		}

		#region Probably won't support

		public void PokeFrame(int frame, IController source)
		{
			throw new NotImplementedException();
		}

		public void ClearFrame(int frame)
		{
			throw new NotImplementedException();
		}

		#endregion
	}
}
