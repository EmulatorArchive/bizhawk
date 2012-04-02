﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BizHawk.Emulation.Consoles.Atari._2600
{
	class mE0 : MapperBase 
	{
		int toggle1 = 0;
		int toggle2 = 0;
		int toggle3 = 0;
		int toggle4 = 0;
		
		public override byte ReadMemory(ushort addr)
		{
			Address(addr);
			if (addr < 0x1000) return base.ReadMemory(addr);
			else if (addr < 0x1400) return core.rom[toggle1 * 1024 + (addr & 0x3FF)];
			else if (addr < 0x1800) return core.rom[toggle2 * 1024 + (addr & 0x3FF)];
			else if (addr < 0x1C00) return core.rom[toggle3 * 1024 + (addr & 0x3FF)];
			else 
				return core.rom[7 * 1024 + (addr & 0x3FF)]; //7 because final bank is always set to last
		}
		public override void WriteMemory(ushort addr, byte value)
		{
			Address(addr);
			if (addr < 0x1000) base.WriteMemory(addr, value);
		}

		public override void SyncState(Serializer ser)
		{
			base.SyncState(ser);
			ser.Sync("toggle1", ref toggle1);
			ser.Sync("toggle2", ref toggle2);
			ser.Sync("toggle3", ref toggle3);
			ser.Sync("toggle4", ref toggle4);
		}

		void Address(ushort addr)
		{
			switch (addr)
			{
				case 0x1FE0:
					toggle1 = 0;
					break;
				case 0x1FE1:
					toggle1 = 1;
					break;
				case 0x1FE2:
					toggle1 = 2;
					break;
				case 0x1FE3:
					toggle1 = 3;
					break;
				case 0x1FE4:
					toggle1 = 4;
					break;
				case 0x1FE5:
					toggle1 = 5;
					break;
				case 0x1FE6:
					toggle1 = 6;
					break;
				case 0x1FE7:
					toggle1 = 7;
					break;

				case 0x1FE8:
					toggle1 = 0;
					break;
				case 0x1FE9:
					toggle1 = 1;
					break;
				case 0x1FEA:
					toggle1 = 2;
					break;
				case 0x1FEB:
					toggle1 = 3;
					break;
				case 0x1FEC:
					toggle1 = 4;
					break;
				case 0x1FED:
					toggle1 = 5;
					break;
				case 0x1FEE:
					toggle1 = 6;
					break;
				case 0x1FEF:
					toggle1 = 7;
					break;

				case 0x1FF0:
					toggle1 = 0;
					break;
				case 0x1FF1:
					toggle1 = 1;
					break;
				case 0x1FF2:
					toggle1 = 2;
					break;
				case 0x1FF3:
					toggle1 = 3;
					break;
				case 0x1FF4:
					toggle1 = 4;
					break;
				case 0x1FF5:
					toggle1 = 5;
					break;
				case 0x1FF6:
					toggle1 = 6;
					break;
				case 0x1FF7:
					toggle1 = 7;
					break;
			}
		}
	}
}
