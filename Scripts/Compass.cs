using static Shiv.Global;

namespace Shiv.Scripts {
	class Compass : Script {
		public override void OnTick() => UI.DrawText(.49f, .01f, $"{AbsHeading(Heading(CameraMatrix)):F0}");
	}
}
