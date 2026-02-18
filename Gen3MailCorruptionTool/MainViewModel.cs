using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Gen3MailCorruptionTool;

public partial class MainViewModel : ObservableObject
{
	[ObservableProperty]
	private uint? _rngFrameMin;

	[ObservableProperty]
	private uint? _rngFrameMax;

	[ObservableProperty]
	private ushort? _trainerId;

	[ObservableProperty]
	private string? _currentMessage;

	private record SearchParameters(uint RngFrameMin, uint RngFrameMax, ushort TrainerId);

	private SearchParameters CollectParameters()
	{
		if (!RngFrameMin.HasValue)
		{
			throw new("RNG Frame Min is invalid");
		}

		if (!RngFrameMax.HasValue)
		{
			throw new("RNG Frame Max is invalid");
		}

		if (!TrainerId.HasValue)
		{
			throw new("Trainer ID is invalid");
		}

		return new(RngFrameMin.Value, RngFrameMax.Value, TrainerId.Value);
	}

	private readonly struct SubstructureOrder
	{
		public readonly int G, A, E, M;

		public SubstructureOrder(uint pid)
		{
			var pidMod = pid % 24;
			switch (pidMod)
			{
				case 0:
					G = 0; A = 1; E = 2; M = 3;
					break;
				case 1:
					G = 0; A = 1; M = 2; E = 3;
					break;
				case 2:
					G = 0; E = 1; A = 2; M = 3;
					break;
				case 3:
					G = 0; E = 1; M = 2; A = 3;
					break;
				case 4:
					G = 0; M = 1; A = 2; E = 3;
					break;
				case 5:
					G = 0; M = 1; E = 2; A = 3;
					break;
				case 6:
					A = 0; G = 1; E = 2; M = 3;
					break;
				case 7:
					A = 0; G = 1; M = 2; E = 3;
					break;
				case 8:
					A = 0; E = 1; G = 2; M = 3;
					break;
				case 9:
					A = 0; E = 1; M = 2; G = 3;
					break;
				case 10:
					A = 0; M = 1; G = 2; E = 3;
					break;
				case 11:
					A = 0; M = 1; E = 2; G = 3;
					break;
				case 12:
					E = 0; G = 1; A = 2; M = 3;
					break;
				case 13:
					E = 0; G = 1; M = 2; A = 3;
					break;
				case 14:
					E = 0; A = 1; G = 2; M = 3;
					break;
				case 15:
					E = 0; A = 1; M = 2; G = 3;
					break;
				case 16:
					E = 0; M = 1; G = 2; A = 3;
					break;
				case 17:
					E = 0; M = 1; A = 2; G = 3;
					break;
				case 18:
					M = 0; G = 1; A = 2; E = 3;
					break;
				case 19:
					M = 0; G = 1; E = 2; A = 3;
					break;
				case 20:
					M = 0; A = 1; G = 2; E = 3;
					break;
				case 21:
					M = 0; A = 1; E = 2; G = 3;
					break;
				case 22:
					M = 0; E = 1; G = 2; A = 3;
					break;
				case 23:
					M = 0; E = 1; A = 2; G = 3;
					break;
			}
		}

		public override string ToString()
		{
			Span<char> ret = stackalloc char[4];
			ret[G] = 'G';
			ret[A] = 'A';
			ret[E] = 'E';
			ret[M] = 'M';
			return new string(ret);
		}
	}

	public readonly record struct EasyChatCorruptionState(uint RngFrame, ushort EasyChatWordCorruption, ushort EasyChatWordChecksumFixFirst, ushort EasyChatWordChecksumFixSecond, uint Pid, uint Ivs);
	private record ComputeCorruptionThreadParam(SearchParameters SearchParameters, uint RngFrameStart, uint RngFrameEnd, ConcurrentBag<EasyChatCorruptionState> ComputedCorruptions);

	private static void ComputeEmeraldCorruptionThreadProc(object? threadParam)
	{
		var param = (ComputeCorruptionThreadParam)threadParam!;

		var seed = 0u;
		for (var rngFrame = 0u; rngFrame < param.RngFrameStart; rngFrame++)
		{
			AdvanceRng(ref seed);
		}

		for (var rngFrame = param.RngFrameStart; rngFrame <= param.RngFrameEnd; rngFrame++)
		{
			var starterRng = seed;
			AdvanceRng(ref seed);

			var pid = (uint)AdvanceRng(ref starterRng);
			pid |= (uint)AdvanceRng(ref starterRng) << 16;

			var substructOrder = new SubstructureOrder(pid);
			if (substructOrder.G != 2)
			{
				// G must be the third substruct
				continue;
			}

			var ivs = (uint)AdvanceRng(ref starterRng) & 0x7FFF;
			ivs |= ((uint)AdvanceRng(ref starterRng) & 0x7FFF) << 15;

			var lowerEncryptionKey = (ushort)(pid ^ param.SearchParameters.TrainerId);

			// easy chat words written are written directly, i.e. "encrypted"
			// so pre-decrypt all the words we'll be using (it makes math easier)
			var easyChatWordsDecrypted = new List<ushort>();
			foreach (var easyChatWordIndex in EasyChat.EmeraldWords.Keys)
			{
				easyChatWordsDecrypted.Add((ushort)(easyChatWordIndex ^ lowerEncryptionKey));
			}

			// Previously there was a search for only 1 easy chat word, but it seems odds are this practically never happens
			// So we always search for two words

			// calculate the word which will correct the checksum

			// assume mudkip (index 283), and only fighting zig, rival, and pooch
			// calculated checksum increases by grabMonSpecies - 283
			// to correct the checksum, decrease by grabMonSpecies - 283
			// (or increase by 0x10000 - (grabMonSpecies - 283) per 2s complement rules)

			// 1st easy chat word is bytes 4-5 in the second substruct
			// if the second substruct is A, those bytes are 0x00BD (Move 3)
			// if the second substruct is E, those bytes are 0x0000 (0 SpAtk EV / 0 SpDef EV; assumption may be broken if lotad or ralts is killed)
			// if the second substruct is M, those bytes depend on the mon (First 16 bits of IVs)

			// 3rd easy chat word is bytes 8-9 in the second substruct
			// if the second substruct is A, those bytes vary depending on move usage (PP moves 1/2)
			// if the second substruct is E, those bytes are 0x0000 (Cute/Smart)
			// if the second substruct is M, those bytes are 0x0000 (Ribbons)

			// 5th easy chat word is species

			// 7th easy chat word is lower 16 bits of exp
			// this is normally 0x0117 (this assumption is broken if you kill a wild mon)

			// 9th easy chat word is pp bonus + friendship (varies with RNG)

			const uint fifthWordExistingData = 0x011B;
			const uint seventhWordExistingData = 0x0117;
			uint firstWordExistingData;
			if (substructOrder.A == 1)
			{
				firstWordExistingData = 0x00BD;
			}
			else if (substructOrder.E == 1)
			{
				firstWordExistingData = 0x0000;
			}
			else if (substructOrder.M == 1)
			{
				firstWordExistingData = ivs & 0xFFFF;
			}
			else
			{
				// should not be reachable
				throw new InvalidOperationException();
			}

			var foundCorruptionFix = false;
			foreach (var easyChatWordDecrypted in easyChatWordsDecrypted)
			{
				// ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
				foreach (var grabMonSpecies in GrabMons.EmeraldSpecies)
				{
					if (easyChatWordDecrypted == grabMonSpecies)
					{
						foreach (var easyChatWordDecryptedFix1 in easyChatWordsDecrypted)
						{
							// ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
							foreach (var easyChatWordDecryptedFix2 in easyChatWordsDecrypted)
							{
								var easyChatWordDecryptedOffset = 0u;
								easyChatWordDecryptedOffset += easyChatWordDecrypted - fifthWordExistingData;
								easyChatWordDecryptedOffset += easyChatWordDecryptedFix1 - firstWordExistingData;
								easyChatWordDecryptedOffset += easyChatWordDecryptedFix2 - seventhWordExistingData;
								if ((easyChatWordDecryptedOffset & 0xFFFF) == 0)
								{
									param.ComputedCorruptions.Add(new(
										RngFrame: rngFrame,
										EasyChatWordCorruption: (ushort)(easyChatWordDecrypted ^ lowerEncryptionKey),
										EasyChatWordChecksumFixFirst: (ushort)(easyChatWordDecryptedFix1 ^ lowerEncryptionKey),
										EasyChatWordChecksumFixSecond: (ushort)(easyChatWordDecryptedFix2 ^ lowerEncryptionKey),
										Pid: pid,
										Ivs: ivs));
									foundCorruptionFix = true;
									break;
								}
							}

							if (foundCorruptionFix)
							{
								break;
							}
						}
					}

					if (foundCorruptionFix)
					{
						break;
					}
				}
			}
		}
	}

	public void ComputeCorruptions()
	{
		SearchParameters parameters;
		try
		{
			parameters = CollectParameters();
		}
		catch (Exception e)
		{
			CurrentMessage = e.Message;
			return;
		}

		_computedCorruptions = [];
		var maxParallelism = Environment.ProcessorCount * 3 / 4;
		var threads = new Thread[maxParallelism];
		var rngFramesPerFrame = (parameters.RngFrameMax - parameters.RngFrameMin + 1) / (uint)maxParallelism;
		if (rngFramesPerFrame > 0)
		{
			for (var i = 0; i < maxParallelism; i++)
			{
				threads[i] = new Thread(ComputeEmeraldCorruptionThreadProc) { IsBackground = true };
				var threadParam = new ComputeCorruptionThreadParam(
					SearchParameters: parameters,
					RngFrameStart: parameters.RngFrameMin + (uint)(i * rngFramesPerFrame),
					RngFrameEnd: parameters.RngFrameMin + (uint)((i + 1) * rngFramesPerFrame - 1),
					ComputedCorruptions: _computedCorruptions);
				threads[i].Start(threadParam);
			}
		}

		// last couple of rng frames covered here
		{
			var threadParam = new ComputeCorruptionThreadParam(
				SearchParameters: parameters,
				RngFrameStart: parameters.RngFrameMin + (uint)maxParallelism * rngFramesPerFrame,
				RngFrameEnd: parameters.RngFrameMax,
				ComputedCorruptions: _computedCorruptions);
			ComputeEmeraldCorruptionThreadProc(threadParam);
		}

		if (rngFramesPerFrame > 0)
		{
			for (var i = 0; i < maxParallelism; i++)
			{
				threads[i].Join();
			}
		}

		if (_computedCorruptions.IsEmpty)
		{
			CurrentMessage = "No corruptions could be computed (add more RNG frames)";
			return;
		}

		// sort computed corruptions into an observable property
		var computedCorruptions = _computedCorruptions.ToArray();
		Array.Sort(computedCorruptions, (x, y) => x.RngFrame.CompareTo(y.RngFrame));

		var sortedComputedCorruptions = new List<ViewableEasyChatCorruptionState>();
		foreach (var computedCorruption in computedCorruptions)
		{
			static string GetNature(uint pid)
			{
				return Natures[pid % 25];
			}

			static string GetStats(uint ivs, uint pid)
			{
				static uint CalcMonStat(uint iv, uint baseStat, uint pidNature, uint statIndex)
				{
					const int MUDKIP_LEVEL = 5;

					if (statIndex == 0)
					{
						// no nature in this case (HP)
						return (2 * baseStat + iv) * MUDKIP_LEVEL / 100 + MUDKIP_LEVEL + 10;
					}

					var stat = (2 * baseStat + iv) * MUDKIP_LEVEL / 100 + 5;
					if (pidNature / 5 == pidNature % 5)
					{
						// neutral nature, don't apply any stat changes
						return stat;
					}

					if (pidNature / 5 == statIndex - 1)
					{
						// nature increase
						stat *= 110;
						stat /= 100;
					}
					else if (pidNature % 5 == statIndex - 1)
					{
						// nature decrease
						stat *= 90;
						stat /= 100;
					}

					return stat;
				}

				var pidNature = pid % 25;
				var hpIv = ivs & 0x1F;
				var hpStat = CalcMonStat(hpIv, 50, pidNature, 0);
				var atkIv = (ivs >> 5) & 0x1F;
				var atkStat = CalcMonStat(atkIv, 70, pidNature, 1);
				var defIv = (ivs >> 10) & 0x1F;
				var defStat = CalcMonStat(defIv, 50, pidNature, 2);
				var spdIv = (ivs >> 15) & 0x1F;
				var spdStat = CalcMonStat(spdIv, 40, pidNature, 3);
				var spAtkIv = (ivs >> 20) & 0x1F;
				var spAtkStat = CalcMonStat(spAtkIv, 50, pidNature, 4);
				var spDefIv = (ivs >> 25) & 0x1F;
				var spDefStat = CalcMonStat(spDefIv, 50, pidNature, 5);
				return $"{hpStat}/{atkStat}/{defStat}/{spAtkStat}/{spDefStat}/{spdStat}";
			}

			sortedComputedCorruptions.Add(new(
				RngFrame: computedCorruption.RngFrame,
				EasyChatWordCorruption: EasyChat.EmeraldWords[computedCorruption.EasyChatWordCorruption],
				EasyChatWordChecksumFixFirst: EasyChat.EmeraldWords[computedCorruption.EasyChatWordChecksumFixFirst],
				EasyChatWordChecksumFixSecond: EasyChat.EmeraldWords[computedCorruption.EasyChatWordChecksumFixSecond],
				Nature: GetNature(computedCorruption.Pid),
				Stats: GetStats(computedCorruption.Ivs, computedCorruption.Pid)));
		}

		SortedComputedCorruptions = sortedComputedCorruptions;
		CurrentMessage = $"Computed {sortedComputedCorruptions.Count} corruptions";
	}

	private ConcurrentBag<EasyChatCorruptionState> _computedCorruptions = [];

	public record ViewableEasyChatCorruptionState(uint RngFrame, string EasyChatWordCorruption, string EasyChatWordChecksumFixFirst, string? EasyChatWordChecksumFixSecond, string Nature, string Stats);

	[ObservableProperty]
	private List<ViewableEasyChatCorruptionState> _sortedComputedCorruptions = [];

	public void Reset()
	{
		_computedCorruptions = [];
		SortedComputedCorruptions = [];
		GC.Collect();
		CurrentMessage = "Corruption computations reset";
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ushort AdvanceRng(ref uint rng)
	{
		rng *= 0x41C64E6D;
		rng += 0x6073;
		return (ushort)(rng >> 16);
	}

	private static readonly string[] Natures =
	[
		"Hardy (Neutral)",
		"Lonely (Atk+/Def-)",
		"Brave (Atk+/Spd-)",
		"Adamant (Atk+/SpAtk-)",
		"Naughty (Atk+/SpDef-)",
		"Bold (Def+/Atk-)",
		"Docile (Neutral)",
		"Relaxed (Def+/Spd-)",
		"Impish (Def+/SpAtk-)",
		"Lax (Def+/SpDef-)",
		"Timid (Spd+/Atk-)",
		"Hasty (Spd+/Def-)",
		"Serious (Neutral)",
		"Jolly (Spd+/SpAtk-)",
		"Naive (Spd+/SpDef-)",
		"Modest (SpAtk+/Atk-)",
		"Mild (SpAtk+/Def-)",
		"Quiet (SpAtk+/Spd-)",
		"Bashful (Neutral)",
		"Rash (SpAtk+/SpDef-)",
		"Calm (SpDef+/Atk-)",
		"Gentle (SpDef+/Def-)",
		"Sassy (SpDef+/Spd-)",
		"Careful (SpDef+/SpAtk-)",
		"Quirky (Neutral)",
	];
}
