using System;
using System.Collections.Generic;

namespace BizHawk.Bizware.BizwareGL
{
	public class PipelineUniform
	{
		internal PipelineUniform(Pipeline owner, UniformInfo info)
		{
			Owner = owner;
			Id = info.Handle;
			SamplerIndex = info.SamplerIndex;
		}

		public Pipeline Owner { get; private set; }
		public IntPtr Id { get; private set; }
		public int SamplerIndex { get; private set; }

		public void Set(Matrix mat, bool transpose = false)
		{
			Owner.Owner.SetPipelineUniformMatrix(this, mat, transpose);
		}

		public void Set(Vector4 vec, bool transpose = false)
		{
			Owner.Owner.SetPipelineUniform(this, vec);
		}

		public void Set(ref Matrix mat, bool transpose = false)
		{
			Owner.Owner.SetPipelineUniformMatrix(this, ref mat, transpose);
		}

		public void Set(Texture2d tex)
		{
			Owner.Owner.SetPipelineUniformSampler(this, tex.Id);
		}
	}
}