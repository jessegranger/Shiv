using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Globals;

namespace Shiv.Scripts {
	class Compass : Script {
		public override void OnTick() {
			UI.DrawText(.49f, .01f, $"{AbsHeading(Heading(CameraMatrix)):F0}");
		}
	}
}
