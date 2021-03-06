﻿/*
Copyright (c) 2019, Lars Brubaker, John Lewin
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;

namespace MatterHackers.MatterControl
{
	public class StagedSetupWindow : DialogWindow
	{
		private FlowLayoutWidget leftPanel;
		private DialogPage activePage;
		private GuiWidget rightPanel;
		private bool footerHeightAcquired = false;
		private ISetupWizard _activeStage;

		private Dictionary<ISetupWizard, WizardStageRow> stageButtons = new Dictionary<ISetupWizard, WizardStageRow>();
		private IStagedSetupWizard setupWizard;

		public ISetupWizard ActiveStage
		{
			get => _activeStage;
			set
			{
				if (_activeStage != null 
					&& stageButtons.TryGetValue(_activeStage, out WizardStageRow activeButton))
				{
					// Mark the leftnav widget as inactive
					activeButton.Active = false;
				}

				// Shutdown the active Wizard
				_activeStage?.Dispose();

				_activeStage = value;

				if (_activeStage == null)
				{
					return;
				}

				if (stageButtons.TryGetValue(_activeStage, out WizardStageRow stageButton))
				{
					stageButton.Active = true;
				}

				// Reset enumerator, move to first item
				_activeStage.Reset();
				_activeStage.MoveNext();

				this.ChangeToPage(_activeStage.Current); ;
			}
		}

		public StagedSetupWindow(IStagedSetupWizard setupWizard)
		{
			this.setupWizard = setupWizard;

			var theme = AppContext.Theme;

			var row = new FlowLayoutWidget()
			{
				HAnchor = HAnchor.Stretch,
				VAnchor = VAnchor.Stretch
			};
			row.AddChild(leftPanel = new FlowLayoutWidget(FlowDirection.TopToBottom)
			{
				BackgroundColor = theme.MinimalShade,
				HAnchor = HAnchor.Absolute,
				VAnchor = VAnchor.Stretch,
				Margin = new BorderDouble(right: theme.DefaultContainerPadding),
				Padding = theme.DefaultContainerPadding,
				Width = 250
			});

			int i = 1;
			foreach (var stage in setupWizard.Stages.Where(s => s.Visible))
			{
				var stageWidget = new WizardStageRow(
					$"{i++}. {stage.Title}",
					"",
					stage,
					theme);
				stageWidget.Enabled = stage.Enabled;
				stageWidget.Click += (s, e) =>
				{
					this.ActiveStage = stage;
				};

				stageButtons.Add(stage, stageWidget);

				leftPanel.AddChild(stageWidget);
			}

			row.AddChild(rightPanel = new GuiWidget()
			{
				HAnchor = HAnchor.Stretch,
				VAnchor = VAnchor.Stretch
			});

			this.Title = setupWizard.Title;

			// Multi-part wizard should not try to resize per page
			this.UseChildWindowSize = false;

			this.AddChild(row);
		}

		public override void ChangeToPage(DialogPage pageToChangeTo)
		{
			if (!footerHeightAcquired)
			{
				GuiWidget footerRow = pageToChangeTo.FindDescendant("FooterRow");
				var fullHeight = footerRow.Height + footerRow.DeviceMarginAndBorder.Height;
				leftPanel.Margin = leftPanel.Margin.Clone(bottom: fullHeight);
				footerHeightAcquired = true;
			}

			activePage = pageToChangeTo;

			pageToChangeTo.DialogWindow = this;
			rightPanel.CloseAllChildren();

			rightPanel.AddChild(pageToChangeTo);

			this.Invalidate();
		}

		public void NextIncompleteStage()
		{
			ISetupWizard nextStage = setupWizard.Stages.FirstOrDefault(s => s.SetupRequired && s.Enabled);

			if (nextStage != null)
			{
				this.ActiveStage = nextStage;
			}
			else
			{
				this.ClosePage();
			}
		}

		public override void ClosePage()
		{
			// Construct and move to the summary/home page
			this.ChangeToPage(setupWizard.HomePageGenerator());

			this.ActiveStage = null;
		}

		public override DialogPage ChangeToPage<PanelType>()
		{
			return base.ChangeToPage<PanelType>();
		}
	}
}