﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using BizHawk.Common;
using BizHawk.Emulation.Common;

using System.Runtime.InteropServices;

using System.IO;


namespace BizHawk.Emulation.Cores.Consoles.Sega.gpgx
{
	public class GPGX : IEmulator, ISyncSoundProvider, IVideoProvider
	{
		static GPGX AttachedCore = null;

		DiscSystem.Disc CD;
		byte[] romfile;

		bool disposed = false;

		LibGPGX.load_archive_cb LoadCallback = null;
		LibGPGX.input_cb InputCallback = null;

		LibGPGX.InputData input = new LibGPGX.InputData();

		// still working out what all the possibilities are here
		public enum ControlType
		{
			None,
			OnePlayer,
			Normal,
			Xea1p,
			Activator,
			Teamplayer,
			Wayplay
		};

		public GPGX(CoreComm NextComm, byte[] romfile, DiscSystem.Disc CD, string romextension, bool sixbutton, ControlType controls)
		{
			// three or six button?
			// http://www.sega-16.com/forum/showthread.php?4398-Forgotten-Worlds-giving-you-GAME-OVER-immediately-Fix-inside&highlight=forgotten%20worlds

			try
			{
				CoreComm = NextComm;
				if (AttachedCore != null)
				{
					AttachedCore.Dispose();
					AttachedCore = null;
				}
				AttachedCore = this;

				LoadCallback = new LibGPGX.load_archive_cb(load_archive);

				this.romfile = romfile;
				this.CD = CD;

				LibGPGX.INPUT_SYSTEM system_a = LibGPGX.INPUT_SYSTEM.SYSTEM_NONE;
				LibGPGX.INPUT_SYSTEM system_b = LibGPGX.INPUT_SYSTEM.SYSTEM_NONE;

				switch (controls)
				{
					case ControlType.None:
					default:
						break;
					case ControlType.Activator:
						system_a = LibGPGX.INPUT_SYSTEM.SYSTEM_ACTIVATOR;
						system_b = LibGPGX.INPUT_SYSTEM.SYSTEM_ACTIVATOR;
						break;
					case ControlType.Normal:
						system_a = LibGPGX.INPUT_SYSTEM.SYSTEM_MD_GAMEPAD;
						system_b = LibGPGX.INPUT_SYSTEM.SYSTEM_MD_GAMEPAD;
						break;
					case ControlType.OnePlayer:
						system_a = LibGPGX.INPUT_SYSTEM.SYSTEM_MD_GAMEPAD;
						break;
					case ControlType.Xea1p:
						system_a = LibGPGX.INPUT_SYSTEM.SYSTEM_XE_A1P;
						break;
					case ControlType.Teamplayer:
						system_a = LibGPGX.INPUT_SYSTEM.SYSTEM_TEAMPLAYER;
						system_b = LibGPGX.INPUT_SYSTEM.SYSTEM_TEAMPLAYER;
						break;
					case ControlType.Wayplay:
						system_a = LibGPGX.INPUT_SYSTEM.SYSTEM_WAYPLAY;
						system_b = LibGPGX.INPUT_SYSTEM.SYSTEM_WAYPLAY;
						break;
				}


				if (!LibGPGX.gpgx_init(romextension, LoadCallback, sixbutton, system_a, system_b))
					throw new Exception("gpgx_init() failed");

				{
					int fpsnum = 60;
					int fpsden = 1;
					LibGPGX.gpgx_get_fps(ref fpsnum, ref fpsden);
					CoreComm.VsyncNum = fpsnum;
					CoreComm.VsyncDen = fpsden;
				}

				savebuff = new byte[LibGPGX.gpgx_state_size()];
				savebuff2 = new byte[savebuff.Length + 13];

				SetControllerDefinition();

				// pull the default video size from the core
				update_video();

				SetMemoryDomains();

				InputCallback = new LibGPGX.input_cb(input_callback);
				LibGPGX.gpgx_set_input_callback(InputCallback);
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		/// <summary>
		/// core callback for file loading
		/// </summary>
		/// <param name="filename">string identifying file to be loaded</param>
		/// <param name="buffer">buffer to load file to</param>
		/// <param name="maxsize">maximum length buffer can hold</param>
		/// <returns>actual size loaded, or 0 on failure</returns>
		int load_archive(string filename, IntPtr buffer, int maxsize)
		{
			byte[] srcdata = null;

			if (buffer == IntPtr.Zero)
			{
				Console.WriteLine("Couldn't satisfy firmware request {0} because buffer == NULL", filename);
				return 0;
			}

			if (filename == "PRIMARY_ROM")
			{
				if (romfile == null)
				{
					Console.WriteLine("Couldn't satisfy firmware request PRIMARY_ROM because none was provided.");
					return 0;
				}
				srcdata = romfile;
			}
			else if (filename == "PRIMARY_CD")
			{
				if (CD == null)
				{
					Console.WriteLine("Couldn't satisfy firmware request PRIMARY_CD because none was provided.");
					return 0;
				}
				srcdata = GetCDData();
				if (srcdata.Length != maxsize)
				{
					Console.WriteLine("Couldn't satisfy firmware request PRIMARY_CD because of struct size.");
					return 0;
				}
			}
			else
			{
				// use fromtend firmware interface

				string firmwareID = null;
				switch (filename)
				{
					case "CD_BIOS_EU": firmwareID = "CD_BIOS_EU"; break;
					case "CD_BIOS_JP": firmwareID = "CD_BIOS_JP"; break;
					case "CD_BIOS_US": firmwareID = "CD_BIOS_US"; break;
					default:
						break;
				}
				if (firmwareID != null)
				{
					srcdata = CoreComm.CoreFileProvider.GetFirmware("GEN", firmwareID, false);
					if (srcdata == null)
					{
						Console.WriteLine("Frontend couldn't satisfy firmware request GEN:{0}", firmwareID);
						return 0;
					}
				}
				else
				{
					Console.WriteLine("Unrecognized firmware request {0}", filename);
					return 0;
				}
			}

			if (srcdata != null)
			{
				if (srcdata.Length > maxsize)
				{
					Console.WriteLine("Couldn't satisfy firmware request {0} because {1} > {2}", filename, srcdata.Length, maxsize);
					return 0;
				}
				else
				{
					Marshal.Copy(srcdata, 0, buffer, srcdata.Length);
					Console.WriteLine("Firmware request {0} satisfied at size {1}", filename, srcdata.Length);
					return srcdata.Length;
				}
			}
			else
			{
				throw new Exception();
				//Console.WriteLine("Couldn't satisfy firmware request {0} for unknown reasons", filename);
				//return 0;
			}

		}

		void CDRead(int lba, IntPtr dest, bool audio)
		{
			if (audio)
			{
				byte[] data = new byte[2352];
				CD.ReadLBA_2352(lba, data, 0);
				Marshal.Copy(data, 0, dest, 2352);
			}
			else
			{
				byte[] data = new byte[2048];
				CD.ReadLBA_2048(lba, data, 0);
				Marshal.Copy(data, 0, dest, 2048);
			}
		}

		LibGPGX.cd_read_cb cd_callback_handle;

		unsafe byte[] GetCDData()
		{
			LibGPGX.CDData ret = new LibGPGX.CDData();
			int size = Marshal.SizeOf(ret);

			ret.readcallback = cd_callback_handle = new LibGPGX.cd_read_cb(CDRead);

			var ses = CD.TOC.Sessions[0];
			int ntrack = ses.Tracks.Count;
	
			// bet you a dollar this is all wrong
			for (int i = 0; i < LibGPGX.CD_MAX_TRACKS; i++)
			{
				if (i < ntrack)
				{
					ret.tracks[i].start = ses.Tracks[i].Indexes[1].aba - 150;
					ret.tracks[i].end = ses.Tracks[i].length_aba + ret.tracks[i].start;
					if (i == ntrack - 1)
					{
						ret.end = ret.tracks[i].end;
						ret.last = ntrack;
					}
				}
				else
				{
					ret.tracks[i].start = 0;
					ret.tracks[i].end = 0;
				}
			}

			byte[] retdata = new byte[size];

			fixed (byte* p = &retdata[0])
			{
				Marshal.StructureToPtr(ret, (IntPtr)p, false);
			}
			return retdata;
		}


		#region controller

		GPGXControlConverter ControlConverter;

		public ControllerDefinition ControllerDefinition { get; private set; }
		public IController Controller { get; set; }

		void SetControllerDefinition()
		{
			if (!LibGPGX.gpgx_get_control(input))
				throw new Exception("gpgx_get_control() failed");

			ControlConverter = new GPGXControlConverter(input);
			ControllerDefinition = ControlConverter.ControllerDef;
		}

		// core callback for input
		void input_callback()
		{
			CoreComm.InputCallback.Call();
			IsLagFrame = false;
		}

		#endregion

		// TODO: use render and rendersound
		public void FrameAdvance(bool render, bool rendersound = true)
		{
			if (Controller["Reset"])
				LibGPGX.gpgx_reset(false);
			if (Controller["Power"])
				LibGPGX.gpgx_reset(true);

			// do we really have to get each time?  nothing has changed
			if (!LibGPGX.gpgx_get_control(input))
				throw new Exception("gpgx_get_control() failed!");

			ControlConverter.Convert(Controller, input);

			if (!LibGPGX.gpgx_put_control(input))
				throw new Exception("gpgx_put_control() failed!");

			IsLagFrame = true;
			Frame++;
			LibGPGX.gpgx_advance();
			update_video();
			update_audio();

			if (IsLagFrame)
				LagCount++;
		}

		public int Frame { get; private set; }
		public int LagCount { get; set; }
		public bool IsLagFrame { get; private set; }

		public string SystemId { get { return "GEN"; } }
		public bool DeterministicEmulation { get { return true; } }
		public string BoardName { get { return null; } }

		public CoreComm CoreComm { get; private set; }

		#region saveram

		byte[] DisposedSaveRam = null;

		public byte[] ReadSaveRam()
		{
			if (disposed)
			{
				return DisposedSaveRam ?? new byte[0];
			}
			else
			{
				int size = 0;
				IntPtr area = IntPtr.Zero;
				LibGPGX.gpgx_get_sram(ref area, ref size);
				if (size <= 0 || area == IntPtr.Zero)
					return new byte[0];
				LibGPGX.gpgx_sram_prepread();

				byte[] ret = new byte[size];
				Marshal.Copy(area, ret, 0, size);
				return ret;
			}
		}

		public void StoreSaveRam(byte[] data)
		{
			if (disposed)
			{
				throw new ObjectDisposedException(typeof(GPGX).ToString());
			}
			else
			{
				int size = 0;
				IntPtr area = IntPtr.Zero;
				LibGPGX.gpgx_get_sram(ref area, ref size);
				if (size <= 0 || area == IntPtr.Zero)
					return;
				if (size != data.Length)
					throw new Exception("Unexpected saveram size");

				Marshal.Copy(data, 0, area, size);
				LibGPGX.gpgx_sram_commitwrite();
			}
		}

		public void ClearSaveRam()
		{
			if (disposed)
			{
				throw new ObjectDisposedException(typeof(GPGX).ToString());
			}
			else
			{
				LibGPGX.gpgx_clear_sram();
			}
		}

		public bool SaveRamModified
		{
			get
			{
				if (disposed)
				{
					return DisposedSaveRam != null;
				}
				else
				{
					int size = 0;
					IntPtr area = IntPtr.Zero;
					LibGPGX.gpgx_get_sram(ref area, ref size);
					return size > 0 && area != IntPtr.Zero;
				}
			}
			set
			{
				throw new Exception();
			}
		}

		#endregion

		public void ResetCounters()
		{
			Frame = 0;
			IsLagFrame = false;
			LagCount = 0;
		}

		#region savestates

		private byte[] savebuff;
		private byte[] savebuff2;

		public void SaveStateText(System.IO.TextWriter writer)
		{
			var temp = SaveStateBinary();
			temp.SaveAsHexFast(writer);
			// write extra copy of stuff we don't use
			writer.WriteLine("Frame {0}", Frame);
		}

		public void LoadStateText(System.IO.TextReader reader)
		{
			string hex = reader.ReadLine();
			if (hex.StartsWith("emuVersion")) // movie save
			{
				do // theoretically, our portion should start right after StartsFromSavestate, maybe...
				{
					hex = reader.ReadLine();
				} while (!hex.StartsWith("StartsFromSavestate"));
				hex = reader.ReadLine();
			}
			byte[] state = new byte[hex.Length / 2];
			state.ReadFromHexFast(hex);
			LoadStateBinary(new System.IO.BinaryReader(new System.IO.MemoryStream(state)));
		}

		public void SaveStateBinary(System.IO.BinaryWriter writer)
		{
			if (!LibGPGX.gpgx_state_save(savebuff, savebuff.Length))
				throw new Exception("gpgx_state_save() returned false");

			writer.Write(savebuff.Length);
			writer.Write(savebuff);
			// other variables
			writer.Write(Frame);
			writer.Write(LagCount);
			writer.Write(IsLagFrame);
		}

		public void LoadStateBinary(System.IO.BinaryReader reader)
		{
			int newlen = reader.ReadInt32();
			if (newlen != savebuff.Length)
				throw new Exception("Unexpected state size");
			reader.Read(savebuff, 0, savebuff.Length);
			if (!LibGPGX.gpgx_state_load(savebuff, savebuff.Length))
				throw new Exception("gpgx_state_load() returned false");
			// other variables
			Frame = reader.ReadInt32();
			LagCount = reader.ReadInt32();
			IsLagFrame = reader.ReadBoolean();
			update_video();
		}

		public byte[] SaveStateBinary()
		{
			var ms = new System.IO.MemoryStream(savebuff2, true);
			var bw = new System.IO.BinaryWriter(ms);
			SaveStateBinary(bw);
			bw.Flush();
			ms.Close();
			return savebuff2;
		}

		public bool BinarySaveStatesPreferred { get { return true; } }

		#endregion

		#region debugging tools

		public MemoryDomainList MemoryDomains { get; private set; }

		unsafe void SetMemoryDomains()
		{
			var mm = new List<MemoryDomain>();
			for (int i = LibGPGX.MIN_MEM_DOMAIN; i <= LibGPGX.MAX_MEM_DOMAIN; i++)
			{
				IntPtr area = IntPtr.Zero;
				int size = 0;
				IntPtr pname = LibGPGX.gpgx_get_memdom(i, ref area, ref size);
				if (area == IntPtr.Zero || pname == IntPtr.Zero || size == 0)
					continue;
				string name = Marshal.PtrToStringAnsi(pname);
				byte *p = (byte*) area;

				mm.Add(new MemoryDomain(name, size, MemoryDomain.Endian.Unknown,
					delegate(int addr)
					{
						if (addr < 0 || addr >= size)
							throw new ArgumentOutOfRangeException();
						return p[addr];
					},
					delegate(int addr, byte val)
					{
						if (addr < 0 || addr >= size)
							throw new ArgumentOutOfRangeException();
						p[addr] = val;
					}));
			}

			MemoryDomains = new MemoryDomainList(mm, 0);
		}


		public List<KeyValuePair<string, int>> GetCpuFlagsAndRegisters()
		{
			return new List<KeyValuePair<string, int>>();
		}

		#endregion

		public void Dispose()
		{
			if (!disposed)
			{
				if (AttachedCore != this)
					throw new Exception();
				if (SaveRamModified)
					DisposedSaveRam = ReadSaveRam();
				AttachedCore = null;
				disposed = true;
			}
		}

		#region SoundProvider

		short[] samples = new short[4096];
		int nsamp = 0;

		public ISoundProvider SoundProvider { get { return null; } }
		public ISyncSoundProvider SyncSoundProvider { get { return this; } }
		public bool StartAsyncSound() { return false; }
		public void EndAsyncSound() { }

		public void GetSamples(out short[] samples, out int nsamp)
		{
			nsamp = this.nsamp;
			samples = this.samples;
			this.nsamp = 0;
		}

		public void DiscardSamples()
		{
			this.nsamp = 0;
		}

		void update_audio()
		{
			IntPtr src = IntPtr.Zero;
			LibGPGX.gpgx_get_audio(ref nsamp, ref src);
			if (src != IntPtr.Zero)
			{
				Marshal.Copy(src, samples, 0, nsamp * 2);
			}
		}

		#endregion

		#region VideoProvider

		public IVideoProvider VideoProvider { get { return this; } }

		int[] vidbuff = new int[0];
		int vwidth;
		int vheight;
		public int[] GetVideoBuffer() { return vidbuff; }
		public int VirtualWidth { get { return BufferWidth; } } // TODO
		public int BufferWidth { get { return vwidth; } }
		public int BufferHeight { get { return vheight; } }
		public int BackgroundColor { get { return unchecked((int)0xff000000); } }

		unsafe void update_video()
		{
			int pitch = 0;
			IntPtr src = IntPtr.Zero;

			LibGPGX.gpgx_get_video(ref vwidth, ref vheight, ref pitch, ref src);

			if (vidbuff.Length < vwidth * vheight)
				vidbuff = new int[vwidth * vheight];

			int rinc = (pitch / 4) - vwidth;
			fixed (int* pdst_ = &vidbuff[0])
			{
				int* pdst = pdst_;
				int* psrc = (int*)src;

				for (int j = 0; j < vheight; j++)
				{
					for (int i = 0; i < vwidth; i++)
						*pdst++ = *psrc++ | unchecked((int)0xff000000);
					psrc += rinc;
				}
			}
		}

		#endregion

		public object GetSettings() { return null; }
		public object GetSyncSettings() { return null; }
		public bool PutSettings(object o) { return false; }
	}
}