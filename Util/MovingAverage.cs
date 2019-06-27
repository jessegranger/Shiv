namespace Shiv {


	public static partial class Global {
		public class MovingAverage {
			public float Value = 0f;
			public int Period;
			public MovingAverage(int period) { Period = period; }
			public void Add(float sample) {
				Value = ((Value * (Period - 1)) + sample) / Period;
			}
		}

	}
}

