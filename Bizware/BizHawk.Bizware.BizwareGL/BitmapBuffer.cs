﻿//TODO - introduce Trim for ArtManager
//TODO - add a small buffer reuse manager.. small images can be stored in larger buffers which we happen to have held. use a timer to wait to free it until some time has passed


using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using sd = System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace BizHawk.Bizware.BizwareGL
{
	/// <summary>
	/// a software-based bitmap, way easier (and faster) to use than .net's built-in bitmap.
	/// Only supports a fixed rgba format
	/// Even though this is IDisposable, you dont have to worry about disposing it normally (that's only for the Bitmap-mimicking)
	/// But you know you can't resist.
	/// </summary>
	public unsafe class BitmapBuffer : IDisposable
	{
		public int Width, Height;
		public int[] Pixels;

		sd.Bitmap WrappedBitmap;
		GCHandle CurrLockHandle;
		BitmapData CurrLock;
		public BitmapData LockBits() //TODO - add read/write semantic, for wraps
		{
			if(CurrLock != null)
				throw new InvalidOperationException("BitmapBuffer can only be locked once!");

			if (WrappedBitmap != null)
			{
				CurrLock = WrappedBitmap.LockBits(new sd.Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
				return CurrLock;
			}

			CurrLockHandle = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
			CurrLock = new BitmapData();
			CurrLock.Height = Height;
			CurrLock.Width = Width;
			CurrLock.Stride = Width * 4;
			CurrLock.Scan0 = CurrLockHandle.AddrOfPinnedObject();

			return CurrLock;
		}

		public void UnlockBits(BitmapData bmpd)
		{
			Debug.Assert(CurrLock == bmpd);

			if (WrappedBitmap != null)
			{
				WrappedBitmap.UnlockBits(CurrLock);
				CurrLock = null;
				return;
			}

			CurrLockHandle.Free();
			CurrLock = null;
		}

		public void Dispose()
		{
			if (CurrLock == null) return;
			UnlockBits(CurrLock);
		}

		public int GetPixel(int x, int y) { return Pixels[Width * y + x]; }
		public void SetPixel(int x, int y, int value) { Pixels[Width * y + x] = value; }
		public Color GetPixelAsColor(int x, int y)
		{
			int c = Pixels[Width * y + x];
			return Color.FromArgb(c);
		}

		/// <summary>
		/// transforms tcol to 0,0,0,0
		/// </summary>
		public void Alphafy(int tcol)
		{
			for (int y = 0, idx = 0; y < Height; y++)
				for (int x = 0; x < Width; x++, idx++)
				{
					if (Pixels[idx] == tcol)
						Pixels[idx] = 0;
				}
		}

		/// <summary>
		/// copies this bitmap and trims out transparent pixels, returning the offset to the topleft pixel
		/// </summary>
		public BitmapBuffer Trim()
		{
			int x, y;
			return Trim(out x, out y);
		}

		/// <summary>
		/// copies this bitmap and trims out transparent pixels, returning the offset to the topleft pixel
		/// </summary>
		public BitmapBuffer Trim(out int xofs, out int yofs)
		{
			int minx = int.MaxValue;
			int maxx = int.MinValue;
			int miny = int.MaxValue;
			int maxy = int.MinValue;
			for (int y = 0; y < Height; y++)
				for (int x = 0; x < Width; x++)
				{
					int pixel = GetPixel(x, y);
					int a = (pixel >> 24) & 0xFF;
					if (a != 0)
					{
						minx = Math.Min(minx, x);
						maxx = Math.Max(maxx, x);
						miny = Math.Min(miny, y);
						maxy = Math.Max(maxy, y);
					}
				}

			if (minx == int.MaxValue || maxx == int.MinValue || miny == int.MaxValue || minx == int.MinValue)
			{
				xofs = yofs = 0;
				return new BitmapBuffer(0, 0);
			}

			int w = maxx - minx + 1;
			int h = maxy - miny + 1;
			BitmapBuffer bbRet = new BitmapBuffer(w, h);
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					bbRet.SetPixel(x, y, GetPixel(x + minx, y + miny));
				}

			xofs = minx;
			yofs = miny;
			return bbRet;
		}

		/// <summary>
		/// increases dimensions of this bitmap to the next higher power of 2
		/// </summary>
		public void Pad()
		{
			int widthRound = nexthigher(Width);
			int heightRound = nexthigher(Height);
			if (widthRound == Width && heightRound == Height) return;
			int[] NewPixels = new int[heightRound * widthRound];

			for (int y = 0, sptr = 0, dptr = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
					NewPixels[dptr++] = Pixels[sptr++];
				dptr += (widthRound - Width);
			}

			Pixels = NewPixels;
			Width = widthRound;
			Height = heightRound;
		}
		
		/// <summary>
		/// Creates a BitmapBuffer image from the specified filename
		/// </summary>
		public BitmapBuffer(string fname, BitmapLoadOptions options)
		{
			using (var fs = new FileStream(fname, FileMode.Open, FileAccess.Read, FileShare.Read))
				LoadInternal(fs, null, options);
		}

		/// <summary>
		/// loads an image from the specified stream
		/// </summary>
		public BitmapBuffer(Stream stream, BitmapLoadOptions options)
		{
			LoadInternal(stream, null, options);
		}

		/// <summary>
		/// Initializes the BitmapBuffer from a System.Drawing.Bitmap
		/// </summary>
		public BitmapBuffer(sd.Bitmap bitmap, BitmapLoadOptions options)
		{
			if (options.AllowWrap && bitmap.PixelFormat == PixelFormat.Format32bppArgb)
			{
				Width = bitmap.Width;
				Height = bitmap.Height;
				WrappedBitmap = bitmap;
			}
			else LoadInternal(null, bitmap, options);
		}

		void LoadInternal(Stream stream, sd.Bitmap bitmap, BitmapLoadOptions options)
		{
			bool cleanup = options.CleanupAlpha0;
			bool needsPad = true;

			var colorKey24bpp = options.ColorKey24bpp;
			using (Bitmap loadedBmp = bitmap == null ? new Bitmap(stream) : null) //sneaky!
			{
				Bitmap bmp = loadedBmp;
				if (bmp == null)
					bmp = bitmap;

				//if we have a 24bpp image and a colorkey callback, the callback can choose a colorkey color and we'll use that
				if (bmp.PixelFormat == PixelFormat.Format24bppRgb && colorKey24bpp != null)
				{
					int colorKey = colorKey24bpp(bmp);
					int w = bmp.Width;
					int h = bmp.Height;
					InitSize(w, h);
					BitmapData bmpdata = bmp.LockBits(new sd.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
					Color[] palette = bmp.Palette.Entries;
					int* ptr = (int*)bmpdata.Scan0.ToPointer();
					int stride = bmpdata.Stride;
					fixed (int* pPtr = &Pixels[0])
					{
						for (int idx = 0, y = 0; y < h; y++)
							for (int x = 0; x < w; x++)
							{
								int srcPixel = ptr[idx];
								if (srcPixel == colorKey)
									srcPixel = 0;
								pPtr[idx++] = srcPixel;
							}
					}

					bmp.UnlockBits(bmpdata);
				}
				if (bmp.PixelFormat == PixelFormat.Format8bppIndexed || bmp.PixelFormat == PixelFormat.Format4bppIndexed)
				{
					int w = bmp.Width;
					int h = bmp.Height;
					InitSize(w, h);
					BitmapData bmpdata = bmp.LockBits(new sd.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
					Color[] palette = bmp.Palette.Entries;
					byte* ptr = (byte*)bmpdata.Scan0.ToPointer();
					int stride = bmpdata.Stride;
					fixed (int* pPtr = &Pixels[0])
					{
						for (int idx = 0, y = 0; y < h; y++)
							for (int x = 0; x < w; x++)
							{
								int srcPixel = ptr[idx];
								if (srcPixel != 0)
								{
									int color = palette[srcPixel].ToArgb();
									
									//make transparent pixels turn into black to avoid filtering issues and other annoying issues with stray junk in transparent pixels.
									//(yes, we can have palette entries with transparency in them (PNGs support this, annoyingly))
									if (cleanup)
									{
										if ((color & 0xFF000000) == 0) color = 0;
										pPtr[idx] = color;
									}
								}
								idx++;
							}
					}

					bmp.UnlockBits(bmpdata);
				}
				else
				{
					//dump the supplied bitmap into our pixels array
					int width = bmp.Width;
					int height = bmp.Height;
					InitSize(width, height);
					BitmapData bmpdata = bmp.LockBits(new sd.Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
					int* ptr = (int*)bmpdata.Scan0.ToInt32();
					int stride = bmpdata.Stride / 4;
					LoadFrom(width, stride, height, (byte*)ptr, options);
					bmp.UnlockBits(bmpdata);
					needsPad = false;
				}
			}

			if (needsPad && options.Pad)
				Pad();
		}


		/// <summary>
		/// Loads the BitmapBuffer from a source buffer, which is expected to be the right pixel format
		/// </summary>
		public unsafe void LoadFrom(int width, int stride, int height, byte* data, BitmapLoadOptions options)
		{
			bool cleanup = options.CleanupAlpha0;
			Width = width;
			Height = height;
			Pixels = new int[width * height];
			fixed (int* pPtr = &Pixels[0])
			{
				for (int idx = 0, y = 0; y < Height; y++)
					for (int x = 0; x < Width; x++)
					{
						int src = y * stride + x;
						int srcVal = ((int*)data)[src];
						
						//make transparent pixels turn into black to avoid filtering issues and other annoying issues with stray junk in transparent pixels
						if (cleanup)
						{
							if ((srcVal & 0xFF000000) == 0) srcVal = 0;
							pPtr[idx++] = srcVal;
						}
					}
			}

			if (options.Pad)
				Pad();
		}

		/// <summary>
		/// premultiplies a color
		/// </summary>
		public static int PremultiplyColor(int srcVal)
		{
			int b = (srcVal >> 0) & 0xFF;
			int g = (srcVal >> 8) & 0xFF;
			int r = (srcVal >> 16) & 0xFF;
			int a = (srcVal >> 24) & 0xFF;
			r = (r * a) >> 8;
			g = (g * a) >> 8;
			b = (b * a) >> 8;
			srcVal = b | (g << 8) | (r << 16) | (a << 24);
			return srcVal;
		}

		/// <summary>
		/// initializes an empty BitmapBuffer, cleared to all 0
		/// </summary>
		public BitmapBuffer(int width, int height)
		{
			InitSize(width, height);
		}

		public BitmapBuffer() { }

		/// <summary>
		/// clears this instance to (0,0,0,0) -- without allocating a new array (to avoid GC churn)
		/// </summary>
		public unsafe void ClearWithoutAlloc()
		{
			//http://techmikael.blogspot.com/2009/12/filling-array-with-default-value.html
			//this guy says its faster

			int size = Width * Height;
			byte fillValue = 0;
			ulong fillValueLong = 0;

			fixed (int* ptr = &Pixels[0])
			{
				ulong* dest = (ulong*)ptr;
				int length = size;
				while (length >= 8)
				{
					*dest = fillValueLong;
					dest++;
					length -= 8;
				}
				byte* bDest = (byte*)dest;
				for (byte i = 0; i < length; i++)
				{
					*bDest = fillValue;
					bDest++;
				}
			}
		}

		/// <summary>
		/// just a temporary measure while refactoring emuhawk
		/// </summary>
		public void AcceptIntArray(int[] arr)
		{
			//should these be copied?
			Pixels = arr;
		}

		/// <summary>
		/// initializes an empty BitmapBuffer, cleared to all 0
		/// </summary>
		public BitmapBuffer(Size size)
		{
			InitSize(size.Width, size.Height);
		}

		void InitSize(int width, int height)
		{
			Pixels = new int[width * height];
			Width = width;
			Height = height;
		}

		/// <summary>
		/// returns the next higher power of 2 than the provided value, for rounding up POW2 textures.
		/// </summary>
		int nexthigher(int k)
		{
			k--;
			for (int i = 1; i < 32; i <<= 1)
				k = k | k >> i;
			int candidate = k + 1;
			return candidate;
		}


		/// <summary>
		/// Dumps this BitmapBuffer to a System.Drawing.Bitmap
		/// </summary>
		public unsafe Bitmap ToSysdrawingBitmap()
		{
			Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
			var bmpdata = bmp.LockBits(new sd.Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

			int* ptr = (int*)bmpdata.Scan0.ToPointer();
			int stride = bmpdata.Stride;
			fixed (int* pPtr = &Pixels[0])
			{
				for (int idx = 0, y = 0; y < Height; y++)
					for (int x = 0; x < Width; x++)
					{
						int srcPixel = pPtr[idx];
						ptr[idx] = srcPixel;
						idx++;
					}
			}

			bmp.UnlockBits(bmpdata);
			return bmp;
		}

	}

}