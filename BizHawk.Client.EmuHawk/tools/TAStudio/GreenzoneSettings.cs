﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using BizHawk.Emulation.Common;
using BizHawk.Emulation.Common.IEmulatorExtensions;
using BizHawk.Client.Common;

namespace BizHawk.Client.EmuHawk
{
	public partial class StateHistorySettingsForm : Form
	{
		public IStatable Statable { get; set; }

		private readonly TasStateManagerSettings Settings;
		private decimal _stateSizeMb;
		public StateHistorySettingsForm(TasStateManagerSettings settings)
		{
			Settings = settings;
			InitializeComponent();
		}

		private void StateHistorySettings_Load(object sender, EventArgs e)
		{
			_stateSizeMb = Statable.SaveStateBinary().Length / (decimal)1024 / (decimal)1024;

			SaveStateHistoryCheckbox.Checked = Settings.SaveStateHistory;
			CapacityNumeric.Value = Settings.Capacitymb == 0 ? 1 : Settings.Capacitymb < CapacityNumeric.Maximum ?
				Settings.Capacitymb :
				CapacityNumeric.Maximum;

			SavestateSizeLabel.Text = Math.Round(_stateSizeMb, 2).ToString() + " mb";
			CapacityNumeric_ValueChanged(null, null);
		}

		private ulong MaxStatesInCapacity
		{
			get { return (ulong)Math.Floor(CapacityNumeric.Value / _stateSizeMb);  }
		}

		private void OkBtn_Click(object sender, EventArgs e)
		{
			Settings.SaveStateHistory = SaveStateHistoryCheckbox.Checked;
			Settings.Capacitymb = (int)CapacityNumeric.Value;
			DialogResult = DialogResult.OK;
			Close();
		}

		private void CancelBtn_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		private void CapacityNumeric_ValueChanged(object sender, EventArgs e)
		{
			NumStatesLabel.Text = MaxStatesInCapacity.ToString();
		}
	}
}
