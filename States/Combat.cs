using System.Linq;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using static Shiv.NavMesh;
using System.Drawing;

namespace Shiv {
	class Combat : State {
		static readonly Blacklist blacklist = new Blacklist("Combat");
		private PedHandle prevTarget = PedHandle.Invalid;
		public Combat(State next):base(next) { }
		public override State OnTick() {
			if( CanControlCharacter() ) {
				var hostile = NearbyHumans().Where(IsInCombat);
				if( hostile.Count() > 0 ) {
					KillTarget = hostile.Without(p => IsInCover(p) && !IsAimingFromCover(p)).Min(DistanceToSelf);
					if( KillTarget == PedHandle.Invalid && !IsInCover(Self) ) {
						return new FindCover(Position(hostile.FirstOrDefault()), this) { Fail = this };
					} else if( prevTarget != KillTarget ) {
						prevTarget = KillTarget;
						WalkTarget = Position(KillTarget);
					}
				}
				return this;
			}
			return Next;
		}
	}
	/*
	class Mission01 : Mission {
		public static int InteriorID = 8706;
		public static readonly Vector3 StartLocation = new Vector3(5310.5f, -5211.87f, 83.52f);
	}
	*/
}
