using System;

internal static class ConsumerInstallationSmokeTokenTests
{
	private static int Main()
	{
		try
		{
			int assertions = 0;
			bool elevated;
			int nativeError;
			Check(ConsumerInstallationSmoke.TryGetCurrentProcessElevation(
				out elevated, out nativeError),
				"The native process-token elevation query must succeed for the current process.",
				ref assertions);
			Check(nativeError == 0,
				"A successful native token query must not retain a Win32 error.",
				ref assertions);

			ConsumerInstallationSmoke.DemandElevationProbeResult(true, true, 0);
			assertions++;
			Expect<InvalidOperationException>(delegate
			{
				ConsumerInstallationSmoke.DemandElevationProbeResult(false, true, 5);
			}, "A failed native query must fail closed.", ref assertions);
			Expect<UnauthorizedAccessException>(delegate
			{
				ConsumerInstallationSmoke.DemandElevationProbeResult(true, false, 0);
			}, "A successfully queried non-elevated token must be rejected.", ref assertions);

			if (elevated)
			{
				ConsumerInstallationSmoke.DemandElevationProbeResult(true, true, 0);
				assertions++;
			}
			else
			{
				Expect<UnauthorizedAccessException>(delegate
				{
					ConsumerInstallationSmoke.DemandElevationProbeResult(true, false, 0);
				}, "The current non-elevated token must remain rejected by the gate.", ref assertions);
			}

			Console.WriteLine("TOKEN ELEVATION PROBE PASS: " + assertions +
				" assertions; current-process elevated=" + elevated + ".");
			return 0;
		}
		catch (Exception error)
		{
			Console.Error.WriteLine("TOKEN ELEVATION PROBE FAIL");
			Console.Error.WriteLine(error);
			return 1;
		}
	}

	private static void Check(bool condition, string message, ref int assertions)
	{
		if (!condition) throw new InvalidOperationException(message);
		assertions++;
	}

	private static void Expect<T>(Action action, string message, ref int assertions)
		where T : Exception
	{
		try
		{
			action();
		}
		catch (T)
		{
			assertions++;
			return;
		}
		throw new InvalidOperationException(message);
	}
}
