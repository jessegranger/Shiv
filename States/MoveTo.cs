using System.Linq;
using System.Numerics;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using static Shiv.NavMesh;
using System.Diagnostics;

namespace Shiv {
	class MoveTo : State {
		PathRequest request;
		Path path;
		NodeHandle Target;
		Stopwatch sw = new Stopwatch();
		public bool Run = false;
		public MoveTo(Vector3 target):this(target, State.Idle) { }
		public MoveTo(Vector3 target, State next):this(Handle(PutOnGround(target, 1f)), next) { }
		public MoveTo(NodeHandle target, State next) : base(next) => request = new PathRequest(PlayerNode, Target = target, 120000, false, true, true, 1);
		public override State OnTick() {
			if( ! CanControlCharacter() ) {
				return Next;
			}
			if( request.IsReady() ) {
				path = request.GetResult();
			}
			if( path == null ) {
				if( request.IsCanceled() ) {
					return null;
				}
				if( request.IsFailed() ) {
					return Fail;
				}
				return this;
			}
			if( path.Count() < 2 ) {
				return Next;
			}
			path.Draw();
			
			switch( FollowPath(path, 0.3f) ) {
				case MoveResult.Continue:
					if( !sw.IsRunning ) {
						sw.Start();
					}
					if( Run && ! IsRunning(Self) ) {
						ToggleSprint();
					}
					Vector3 step = Position(path.First());
					var vel = Vector3.Normalize(Velocity(Self));
					var expected = Vector3.Normalize(step - PlayerPosition);
					var overlap = Vector3.Dot(vel, expected);
					// UI.DrawTextInWorld(step, $"Overlap: {overlap}");
					if( sw.ElapsedMilliseconds > 3200 && overlap <= 0.01f && !IsClimbing(Self) && !IsJumping(Self) && !Call<bool>(IS_PED_RAGDOLL, Self) ) {
						Log($"STUCK at {sw.ElapsedMilliseconds} overlap {overlap}");
						Stuck();
					}
					break;
				case MoveResult.Complete:
					path.Pop();
					sw.Restart();
					break;
				case MoveResult.Failed:
					Log("MoveResult.Failed");
					Stuck();
					return Fail;
			}
			return this;
		}
		private void Stuck() {
			if( path != null ) {
				var stuck = path.Skip(1).FirstOrDefault();
				var stuckPos = Position(stuck);
				Flood(stuck, 30, 30, default, Edges)
					.Without(PlayerNode)
					.Where(n => (stuckPos - Position(n)).LengthSquared() < .75f )
					.ToArray() // read it all so the first block doesn't interrupt the Flood
					.Each(Block);
			}
			Restart();
		}
		private void Restart() {
			path = null;
			request = new PathRequest(PlayerNode, Target, 120000, false, true, true, 1);
			sw.Reset();
		}
	}
}
