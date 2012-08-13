﻿using System;
using System.IO;
using System.Collections.Generic;
using BizHawk.Emulation.CPUs.CP1610;

namespace BizHawk.Emulation.Consoles.Intellivision
{
	public sealed partial class Intellivision : IEmulator
	{
		byte[] Rom;
		GameInfo Game;

		CP1610 Cpu;
		ICart Cart;
		STIC Stic;
		PSG Psg;

		private bool Sr1ToIntRM, Sr2ToBusRq, BusAkToSst;

		private bool GetSr1ToIntRM()
		{
			return Sr1ToIntRM;
		}

		private bool GetSr2ToBusRq()
		{
			return Sr2ToBusRq;
		}

		private bool GetBusAkToSst()
		{
			return BusAkToSst;
		}

		private void SetSr1ToIntRM(bool value)
		{
			Sr1ToIntRM = value;
		}

		private void SetSr2ToBusRq(bool value)
		{
			Sr2ToBusRq = value;
		}

		private void SetBusAkToSst(bool value)
		{
			BusAkToSst = value;
		}

		public void LoadExecutiveRom()
		{
			FileStream fs = new FileStream("C:/erom.int", FileMode.Open, FileAccess.Read);
			BinaryReader r = new BinaryReader(fs);
			byte[] erom = r.ReadBytes(8192);
			int index = 0;
			// Combine every two bytes into a word.
			while (index + 1 < erom.Length)
				ExecutiveRom[index / 2] = (ushort)((erom[index++] << 8) | erom[index++]);
			r.Close();
			fs.Close();
		}

		public void LoadGraphicsRom()
		{
			FileStream fs = new FileStream("C:/grom.int", FileMode.Open, FileAccess.Read);
			BinaryReader r = new BinaryReader(fs);
			byte[] grom = r.ReadBytes(2048);
			for (int index = 0; index < grom.Length; index++)
				GraphicsRom[index] = grom[index];
			r.Close();
			fs.Close();
		}

		public Intellivision(GameInfo game, byte[] rom)
		{
			Rom = rom;
			Game = game;
			LoadExecutiveRom();
			LoadGraphicsRom();
			Cart = new Intellicart();
			if (Cart.Parse(Rom) == -1)
			{
				Cart = new Cartridge();
				Cart.Parse(Rom);
			}

			Cpu = new CP1610();
			Cpu.ReadMemory = ReadMemory;
			Cpu.WriteMemory = WriteMemory;
			Cpu.GetIntRM = GetSr1ToIntRM;
			Cpu.GetBusRq = GetSr2ToBusRq;
			Cpu.GetBusAk = GetBusAkToSst;
			Cpu.SetBusAk = SetBusAkToSst;
			Cpu.Reset();

			Stic = new STIC();
			Stic.GetSr1 = GetSr1ToIntRM;
			Stic.GetSr2 = GetSr2ToBusRq;
			Stic.GetSst = GetBusAkToSst;
			Stic.SetSr1 = SetSr1ToIntRM;
			Stic.SetSr2 = SetSr2ToBusRq;
			Stic.Reset();

			Psg = new PSG();

			CoreOutputComm = new CoreOutputComm();

			Cpu.LogData();
		}

		public void FrameAdvance(bool render)
		{
			Cpu.Execute(999);
		}



		// This is all crap to worry about later.

		public IVideoProvider VideoProvider { get { return new NullEmulator(); } }
		public ISoundProvider SoundProvider { get { return NullSound.SilenceProvider; } }

		public ControllerDefinition ControllerDefinition
		{
			get { return null; }
		}

		public IController Controller { get; set; }


		public int Frame
		{
			get { return 0; }
		}

		public int LagCount
		{
			get { return 0; }
			set { }
		}

		public bool IsLagFrame { get { return false; } }
		public string SystemId
		{
			get { return "INTV"; }
		}

		public bool DeterministicEmulation { get; set; }

		public byte[] SaveRam { get { return null; } }

		public bool SaveRamModified
		{
			get { return false; }
			set { }
		}

		public void ResetFrameCounter()
		{
		}

		public void SaveStateText(TextWriter writer)
		{
			throw new NotImplementedException();
		}

		public void LoadStateText(TextReader reader)
		{
			throw new NotImplementedException();
		}

		public void SaveStateBinary(BinaryWriter writer)
		{
			throw new NotImplementedException();
		}

		public void LoadStateBinary(BinaryReader reader)
		{
			throw new NotImplementedException();
		}

		public byte[] SaveStateBinary()
		{
			return new byte[0];
		}

		public CoreInputComm CoreInputComm { get; set; }
		public CoreOutputComm CoreOutputComm { get; private set; }

		public IList<MemoryDomain> MemoryDomains
		{
			get { throw new NotImplementedException(); }
		}

		public MemoryDomain MainMemory
		{
			get { throw new NotImplementedException(); }
		}

		public void Dispose()
		{
		}
	}
}