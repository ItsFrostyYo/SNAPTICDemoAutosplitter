using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LiveSplit.SNAPTICDemo
{
    internal sealed class GodotMemoryReader : IDisposable
    {
        private const int VariantSize = 24;
        private const int LevelLoaderRecordSize = 11 * VariantSize;
        private const int PlayerRecordSize = 59 * VariantSize;
        private const int CutsceneStarterRecordSize = 10 * VariantSize;
        private const int Node3DGlobalPositionOffset = 0x39C;
        private const int NodeChildrenCacheOffset = 0x188;
        private const int MirraxisPlayerChildIndex = 6;
        private const int PlayerCurrentBodyMember = 57;
        private const long MaximumUserAddress = 0x0000800000000000L;
        private const float UpperPlatformMinX = 342.0f;
        private const float UpperPlatformMaxX = 352.0f;
        private const float UpperPlatformMinY = 220.5f;
        private const float UpperPlatformMaxY = 221.75f;
        private const float UpperPlatformMinZ = -126.0f;
        private const float UpperPlatformMaxZ = -116.0f;
        private const float SkyPotMinY = 216.4f;
        private const float SkyPotMaxY = 225.25f;
        private const float SkyPotCenterX = 347.0f;
        private const float SkyPotCenterZ = -121.0f;
        private const float SkyPotContactRadius = 2.2f;

        private Process process;
        private IntPtr levelLoaderMembers;
        private IntPtr playerMembers;
        private IntPtr cutsceneStarterMembers;
        private Task<IntPtr> levelScanTask;
        private CancellationTokenSource levelScanCancellation;
        private Task<IntPtr> playerScanTask;
        private CancellationTokenSource playerScanCancellation;
        private DateTime nextHookAttempt;
        private DateTime nextScanAttempt;
        private DateTime nextPlayerScanAttempt;
        private long? previousLevel;
        private Position? previousPlayerPosition;
        private bool cutsceneRequestWasActive;
        private bool startSignal;
        private bool endSplitSignal;
        private bool resetSignal;
        private bool disposed;
        private bool wasInEndRegion;
        private bool hadValidPlayerBody;

        public void Update()
        {
            if (disposed)
                return;

            startSignal = false;
            resetSignal = false;

            if (!IsProcessAlive())
            {
                Detach();
                TryHook();
                return;
            }

            UpdateLevelScan();
            if (levelLoaderMembers == IntPtr.Zero)
                return;

            long currentLevel;
            if (!TryReadInt64(IntPtr.Add(levelLoaderMembers, 0xE0), out currentLevel) ||
                currentLevel < 0 || currentLevel > 7)
            {
                SNAPTICDemoLog.Write("LevelLoader pointer became invalid; clearing cached pointers and rescanning.");
                levelLoaderMembers = IntPtr.Zero;
                previousLevel = null;
                cutsceneStarterMembers = IntPtr.Zero;
                ClearPlayerTracking();
                cutsceneRequestWasActive = false;
                nextScanAttempt = DateTime.UtcNow;
                return;
            }

            if (!previousLevel.HasValue || previousLevel.Value != currentLevel)
                SNAPTICDemoLog.Write("Level changed: " +
                    (previousLevel.HasValue ? previousLevel.Value.ToString() : "<unknown>") +
                    " -> " + currentLevel + ".");

            if (previousLevel.HasValue && previousLevel.Value != 0 && currentLevel == 0)
            {
                resetSignal = true;
                SNAPTICDemoLog.Write("Main-menu transition detected; RESET signal latched.");
            }
            previousLevel = currentLevel;

            if (currentLevel != 5)
            {
                cutsceneStarterMembers = IntPtr.Zero;
                ClearPlayerTracking();
                cutsceneRequestWasActive = false;
                return;
            }

            UpdateEndSplit();

            if (cutsceneStarterMembers == IntPtr.Zero)
            {
                cutsceneStarterMembers = ResolveMainCutsceneStarter();
                if (cutsceneStarterMembers == IntPtr.Zero)
                    return;
            }

            byte[] starter;
            if (!TryReadBlock(cutsceneStarterMembers, CutsceneStarterRecordSize, out starter) ||
                !IsMainCutsceneStarterMembers(starter, 0))
            {
                cutsceneStarterMembers = IntPtr.Zero;
                cutsceneRequestWasActive = false;
                return;
            }

            long requestObject = BitConverter.ToInt64(starter, 0xE8);
            startSignal = cutsceneRequestWasActive && requestObject == 0;
            if (startSignal)
                SNAPTICDemoLog.Write("Opening cutscene request ended; START signal latched.");
            cutsceneRequestWasActive = requestObject != 0;
        }

        public bool ConsumeStartSignal()
        {
            bool result = startSignal;
            startSignal = false;
            return result;
        }

        public bool ConsumeResetSignal()
        {
            bool result = resetSignal;
            resetSignal = false;
            return result;
        }

        public bool ConsumeEndSplitSignal()
        {
            bool result = endSplitSignal;
            endSplitSignal = false;
            return result;
        }

        private bool IsProcessAlive()
        {
            try
            {
                return process != null && !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private void TryHook()
        {
            if (DateTime.UtcNow < nextHookAttempt)
                return;
            nextHookAttempt = DateTime.UtcNow.AddSeconds(1);

            foreach (Process candidate in Process.GetProcessesByName("snaptic"))
            {
                try
                {
                    if (candidate.HasExited || !Is64Bit(candidate))
                        continue;

                    string fileName = Path.GetFileName(candidate.MainModule.FileName);
                    if (!string.Equals(fileName, "snaptic.exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    process = candidate;
                    previousLevel = null;
                    nextScanAttempt = DateTime.UtcNow;
                    SNAPTICDemoLog.Write("Attached to snaptic.exe PID " + candidate.Id + ".");
                    return;
                }
                catch
                {
                    candidate.Dispose();
                }
            }
        }

        private void UpdateLevelScan()
        {
            if (levelLoaderMembers != IntPtr.Zero)
                return;

            if (levelScanTask != null)
            {
                if (!levelScanTask.IsCompleted)
                    return;

                try
                {
                    levelLoaderMembers = levelScanTask.Result;
                    if (levelLoaderMembers != IntPtr.Zero)
                        SNAPTICDemoLog.Write("LevelLoader members found at 0x" +
                            levelLoaderMembers.ToInt64().ToString("X") + ".");
                    else
                        nextScanAttempt = DateTime.UtcNow.AddSeconds(1);
                }
                catch (Exception exception)
                {
                    SNAPTICDemoLog.Write("ERROR: LevelLoader scan failed: " + exception + ".");
                    nextScanAttempt = DateTime.UtcNow.AddSeconds(1);
                }
                finally
                {
                    levelScanTask = null;
                    levelScanCancellation?.Dispose();
                    levelScanCancellation = null;
                }
                return;
            }

            if (DateTime.UtcNow < nextScanAttempt)
                return;

            Process attachedProcess = process;
            levelScanCancellation = new CancellationTokenSource();
            CancellationToken token = levelScanCancellation.Token;
            levelScanTask = Task.Run(
                () => FindLevelLoader(attachedProcess, token),
                token);
        }

        private void UpdateEndSplit()
        {
            if (playerMembers == IntPtr.Zero)
            {
                playerMembers = ResolvePlayerMembers();
                if (playerMembers == IntPtr.Zero)
                {
                    UpdatePlayerScan();
                    if (playerMembers == IntPtr.Zero)
                        return;
                }
                else
                {
                    StopPlayerScan();
                }
                SNAPTICDemoLog.Write("Player members found at 0x" +
                    playerMembers.ToInt64().ToString("X") + ".");
            }

            byte[] player;
            if (!TryReadBlock(playerMembers, PlayerRecordSize, out player) ||
                !IsPlayerMembers(player, 0))
            {
                SNAPTICDemoLog.Write("Player member pointer became invalid; clearing player tracking and resolving again.");
                ClearPlayerTracking();
                return;
            }

            long bodyObject = BitConverter.ToInt64(
                player,
                PlayerCurrentBodyMember * VariantSize + 16);
            if (!IsPlausiblePointer(bodyObject))
            {
                if (hadValidPlayerBody)
                    SNAPTICDemoLog.Write("Player current_body is no longer a valid object pointer.");
                hadValidPlayerBody = false;
                previousPlayerPosition = null;
                wasInEndRegion = false;
                return;
            }
            if (!hadValidPlayerBody)
                SNAPTICDemoLog.Write("Player current_body resolved at 0x" + bodyObject.ToString("X") + ".");
            hadValidPlayerBody = true;
            Position currentPosition;
            if (!TryReadPosition(
                    new IntPtr(bodyObject + Node3DGlobalPositionOffset),
                    out currentPosition))
            {
                SNAPTICDemoLog.Write("WARNING: Failed to read a plausible Player global position.");
                previousPlayerPosition = null;
                wasInEndRegion = false;
                return;
            }

            bool inEndRegion = IsInUpperPlatformBounds(currentPosition) ||
                IsInSkyPotBounds(currentPosition);
            bool crossedEndRegion = previousPlayerPosition.HasValue &&
                (SegmentIntersectsUpperPlatformBounds(
                    previousPlayerPosition.Value,
                    currentPosition) ||
                 SegmentIntersectsSkyPotBounds(
                    previousPlayerPosition.Value,
                    currentPosition));
            if (inEndRegion && !wasInEndRegion)
            {
                SNAPTICDemoLog.Write(
                    "Player entered End Split region at " + FormatPosition(currentPosition) + ". " +
                    "UpperPlatform=" + IsInUpperPlatformBounds(currentPosition) +
                    ", SkyPot=" + IsInSkyPotBounds(currentPosition) + ".");
            }
            else if (!inEndRegion && wasInEndRegion)
            {
                SNAPTICDemoLog.Write("Player left End Split region at " + FormatPosition(currentPosition) + ".");
            }

            if (crossedEndRegion && !inEndRegion)
            {
                SNAPTICDemoLog.Write(
                    "Player movement segment crossed End Split region between " +
                    FormatPosition(previousPlayerPosition.Value) + " and " +
                    FormatPosition(currentPosition) + ".");
            }

            if (inEndRegion || crossedEndRegion)
            {
                if (!endSplitSignal)
                    SNAPTICDemoLog.Write("END SPLIT signal latched by position/contact detection.");
                endSplitSignal = true;
            }

            wasInEndRegion = inEndRegion;
            previousPlayerPosition = currentPosition;
        }

        private void UpdatePlayerScan()
        {
            if (playerMembers != IntPtr.Zero)
                return;

            if (playerScanTask != null)
            {
                if (!playerScanTask.IsCompleted)
                    return;

                try
                {
                    playerMembers = playerScanTask.Result;
                    if (playerMembers == IntPtr.Zero)
                        nextPlayerScanAttempt = DateTime.UtcNow.AddSeconds(1);
                }
                catch (Exception exception)
                {
                    SNAPTICDemoLog.Write("ERROR: Player scan failed: " + exception + ".");
                    nextPlayerScanAttempt = DateTime.UtcNow.AddSeconds(1);
                }
                finally
                {
                    playerScanTask = null;
                    playerScanCancellation?.Dispose();
                    playerScanCancellation = null;
                }
                return;
            }

            if (DateTime.UtcNow < nextPlayerScanAttempt)
                return;

            Process attachedProcess = process;
            long addressHint = levelLoaderMembers.ToInt64();
            playerScanCancellation = new CancellationTokenSource();
            CancellationToken token = playerScanCancellation.Token;
            playerScanTask = Task.Run(
                () => FindPlayer(attachedProcess, token, addressHint),
                token);
            SNAPTICDemoLog.Write("Player reference was not cached by the level; started fallback memory scan.");
        }

        private IntPtr ResolveMainCutsceneStarter()
        {
            // LevelLoader.current_status -> LoadStatus.scene -> Mirraxis
            // -> cutscene_starter. Godot OBJECT Variants store Object* at +16.
            long sceneObject;
            long sceneMembers;
            byte[] scene;
            if (!TryResolveMirraxisSceneMembers(
                    out sceneObject,
                    out sceneMembers,
                    out scene))
                return IntPtr.Zero;

            long starterObject = BitConverter.ToInt64(scene, 0x88);
            long starterMembers = GetScriptMembers(starterObject);
            byte[] starter;
            if (!TryReadBlock(new IntPtr(starterMembers), CutsceneStarterRecordSize, out starter) ||
                !IsMainCutsceneStarterMembers(starter, 0))
                return IntPtr.Zero;

            return new IntPtr(starterMembers);
        }

        private IntPtr ResolvePlayerMembers()
        {
            long sceneObject;
            long sceneMembers;
            byte[] scene;
            if (!TryResolveMirraxisSceneMembers(
                    out sceneObject,
                    out sceneMembers,
                    out scene))
                return IntPtr.Zero;

            // Mirraxis._player is only initialized by a late music-phase branch,
            // so it can remain nil during direct-teleport and glitched routes.
            // Prefer it when available, then resolve the actual Player node from
            // Mirraxis's stable root-child array (Player is child index 6).
            long playerObject = HasType(scene, 0x48, 24)
                ? BitConverter.ToInt64(scene, 0x58)
                : 0;
            IntPtr members = ValidatePlayerObject(playerObject);
            if (members != IntPtr.Zero)
                return members;

            long childrenCache;
            if (!TryReadInt64(
                    new IntPtr(sceneObject + NodeChildrenCacheOffset),
                    out childrenCache) ||
                !IsPlausiblePointer(childrenCache) ||
                !TryReadInt64(
                    new IntPtr(childrenCache + MirraxisPlayerChildIndex * 8),
                    out playerObject))
                return IntPtr.Zero;

            return ValidatePlayerObject(playerObject);
        }

        private IntPtr ValidatePlayerObject(long playerObject)
        {
            long members = GetScriptMembers(playerObject);
            byte[] player;
            if (!TryReadBlock(new IntPtr(members), PlayerRecordSize, out player) ||
                !IsPlayerMembers(player, 0))
                return IntPtr.Zero;

            return new IntPtr(members);
        }

        private bool TryResolveMirraxisSceneMembers(
            out long sceneObject,
            out long sceneMembers,
            out byte[] scene)
        {
            sceneObject = 0;
            sceneMembers = 0;
            scene = null;

            long statusObject;
            if (!TryReadInt64(IntPtr.Add(levelLoaderMembers, 0x100), out statusObject))
                return false;

            long statusMembers = GetScriptMembers(statusObject);
            byte[] status;
            if (!TryReadBlock(new IntPtr(statusMembers), 9 * VariantSize, out status) ||
                !HasType(status, 0x00, 4) ||
                !HasType(status, 0x30, 25) ||
                !HasType(status, 0x48, 24) ||
                !HasType(status, 0x60, 3) ||
                !HasType(status, 0x78, 2))
                return false;

            sceneObject = BitConverter.ToInt64(status, 0x58);
            sceneMembers = GetScriptMembers(sceneObject);
            return TryReadBlock(new IntPtr(sceneMembers), 6 * VariantSize, out scene) &&
                HasType(scene, 0x00, 1) &&
                HasType(scene, 0x18, 1) &&
                HasType(scene, 0x30, 24) &&
                IsNilOrObject(scene, 0x48) &&
                HasType(scene, 0x60, 24) &&
                HasType(scene, 0x78, 24);
        }

        private bool TryReadPosition(IntPtr address, out Position position)
        {
            position = default(Position);
            byte[] data;
            if (!TryReadBlock(address, 12, out data))
                return false;

            position = new Position(
                BitConverter.ToSingle(data, 0),
                BitConverter.ToSingle(data, 4),
                BitConverter.ToSingle(data, 8));
            return position.IsPlausible;
        }

        private static bool IsInUpperPlatformBounds(Position position)
        {
            // The checkpoint core is 6 x 6, but live edge coordinates show the
            // walkable platform reaches about five units from its center at
            // (347, 221, -121). The narrow Y slab still begins at contact, so
            // the wider footprint does not trigger while approaching overhead.
            return IsInBounds(
                position,
                UpperPlatformMinX, UpperPlatformMaxX,
                UpperPlatformMinY, UpperPlatformMaxY,
                UpperPlatformMinZ, UpperPlatformMaxZ);
        }

        private static bool IsInSkyPotBounds(Position position)
        {
            // UpperPot2 is upright from about Y=221 to Y=224.3, while the
            // invisible SkyPot is inverted below it from about Y=220 to Y=216.7.
            // Their teleporter cylinder has a radius of about 1.1; the remaining
            // radius accounts for the player's collision body touching its wall.
            float x = position.X - SkyPotCenterX;
            float z = position.Z - SkyPotCenterZ;
            return position.Y >= SkyPotMinY && position.Y <= SkyPotMaxY &&
                x * x + z * z <= SkyPotContactRadius * SkyPotContactRadius;
        }

        private static bool SegmentIntersectsUpperPlatformBounds(
            Position start,
            Position end)
        {
            return SegmentIntersectsBounds(
                start, end,
                UpperPlatformMinX, UpperPlatformMaxX,
                UpperPlatformMinY, UpperPlatformMaxY,
                UpperPlatformMinZ, UpperPlatformMaxZ);
        }

        private static bool SegmentIntersectsSkyPotBounds(Position start, Position end)
        {
            float enter = 0.0f;
            float exit = 1.0f;
            if (!ClipSegmentAxis(
                    start.Y,
                    end.Y - start.Y,
                    SkyPotMinY,
                    SkyPotMaxY,
                    ref enter,
                    ref exit))
                return false;

            float startX = start.X - SkyPotCenterX;
            float startZ = start.Z - SkyPotCenterZ;
            float deltaX = end.X - start.X;
            float deltaZ = end.Z - start.Z;
            float a = deltaX * deltaX + deltaZ * deltaZ;
            float radiusSquared = SkyPotContactRadius * SkyPotContactRadius;
            if (a < 0.0001f)
                return startX * startX + startZ * startZ <= radiusSquared;

            float b = 2.0f * (startX * deltaX + startZ * deltaZ);
            float c = startX * startX + startZ * startZ - radiusSquared;
            float discriminant = b * b - 4.0f * a * c;
            if (discriminant < 0.0f)
                return false;

            float root = (float)Math.Sqrt(discriminant);
            float circleEnter = (-b - root) / (2.0f * a);
            float circleExit = (-b + root) / (2.0f * a);
            enter = Math.Max(enter, circleEnter);
            exit = Math.Min(exit, circleExit);
            return enter <= exit;
        }

        private static bool IsInBounds(
            Position position,
            float minX,
            float maxX,
            float minY,
            float maxY,
            float minZ,
            float maxZ)
        {
            return position.X >= minX && position.X <= maxX &&
                position.Y >= minY && position.Y <= maxY &&
                position.Z >= minZ && position.Z <= maxZ;
        }

        private static bool SegmentIntersectsBounds(
            Position start,
            Position end,
            float minX,
            float maxX,
            float minY,
            float maxY,
            float minZ,
            float maxZ)
        {
            float enter = 0.0f;
            float exit = 1.0f;
            return ClipSegmentAxis(start.X, end.X - start.X, minX, maxX, ref enter, ref exit) &&
                ClipSegmentAxis(start.Y, end.Y - start.Y, minY, maxY, ref enter, ref exit) &&
                ClipSegmentAxis(start.Z, end.Z - start.Z, minZ, maxZ, ref enter, ref exit);
        }

        private static bool ClipSegmentAxis(
            float start,
            float delta,
            float minimum,
            float maximum,
            ref float enter,
            ref float exit)
        {
            if (Math.Abs(delta) < 0.0001f)
                return start >= minimum && start <= maximum;

            float first = (minimum - start) / delta;
            float second = (maximum - start) / delta;
            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }

            enter = Math.Max(enter, first);
            exit = Math.Min(exit, second);
            return enter <= exit;
        }

        private void ClearPlayerTracking()
        {
            StopPlayerScan();
            playerMembers = IntPtr.Zero;
            previousPlayerPosition = null;
            endSplitSignal = false;
            wasInEndRegion = false;
            hadValidPlayerBody = false;
        }

        private void StopPlayerScan()
        {
            playerScanCancellation?.Cancel();
            playerScanCancellation?.Dispose();
            playerScanCancellation = null;
            playerScanTask = null;
            nextPlayerScanAttempt = DateTime.MinValue;
        }

        private long GetScriptMembers(long objectAddress)
        {
            if (!IsPlausiblePointer(objectAddress))
                return 0;

            // Offsets for Godot 4.7.1 stable custom build used by SNAPTIC 0.84.
            long scriptInstance;
            if (!TryReadInt64(new IntPtr(objectAddress + 0x60), out scriptInstance) ||
                !IsPlausiblePointer(scriptInstance))
                return 0;

            long members;
            if (!TryReadInt64(new IntPtr(scriptInstance + 0x28), out members) ||
                !IsPlausiblePointer(members))
                return 0;
            return members;
        }

        private bool TryReadInt64(IntPtr address, out long value)
        {
            value = 0;
            byte[] data;
            if (!TryReadBlock(address, 8, out data))
                return false;
            value = BitConverter.ToInt64(data, 0);
            return true;
        }

        private bool TryReadBlock(IntPtr address, int size, out byte[] data)
        {
            data = null;
            if (process == null || !IsPlausibleAddress(address.ToInt64()))
                return false;

            byte[] buffer = new byte[size];
            UIntPtr bytesRead;
            if (!ReadProcessMemory(
                    process.Handle,
                    address,
                    buffer,
                    new UIntPtr((uint)size),
                    out bytesRead) ||
                bytesRead.ToUInt64() != (ulong)size)
                return false;

            data = buffer;
            return true;
        }

        private static IntPtr FindLevelLoader(Process game, CancellationToken token)
        {
            return FindMembers(game, token, LevelLoaderRecordSize, IsLevelLoaderMembers);
        }

        private static IntPtr FindPlayer(
            Process game,
            CancellationToken token,
            long addressHint)
        {
            // Godot's live script instances are normally allocated in the same
            // broad heap range. Search around the already-known LevelLoader first
            // so a late-game/direct-teleport test does not wait on a full scan.
            const ulong NearbyRadius = 0x400000000UL;
            const ulong Maximum = 0x00007FFFFFFFFFFFUL;
            ulong center = (ulong)Math.Max(addressHint, 0);
            ulong start = center > NearbyRadius ? center - NearbyRadius : 0;
            ulong end = center < Maximum - NearbyRadius
                ? center + NearbyRadius
                : Maximum;

            IntPtr nearby = FindMembers(
                game,
                token,
                PlayerRecordSize,
                IsPlayerMembers,
                start,
                end);
            if (nearby != IntPtr.Zero || token.IsCancellationRequested)
                return nearby;

            return FindMembers(game, token, PlayerRecordSize, IsPlayerMembers);
        }

        private static IntPtr FindMembers(
            Process game,
            CancellationToken token,
            int recordSize,
            Func<byte[], int, bool> isMatch,
            ulong startAddress = 0,
            ulong maximum = 0x00007FFFFFFFFFFFUL)
        {
            const uint MemCommit = 0x1000;
            const uint MemPrivate = 0x20000;
            const uint PageGuard = 0x100;
            const uint PageNoAccess = 0x01;
            const int ChunkSize = 4 * 1024 * 1024;

            ulong address = startAddress;
            int querySize = Marshal.SizeOf(typeof(MemoryBasicInformation));

            while (address < maximum && !token.IsCancellationRequested)
            {
                MemoryBasicInformation info;
                int queried = VirtualQueryEx(
                    game.Handle,
                    new IntPtr((long)address),
                    out info,
                    new UIntPtr((uint)querySize));
                if (queried == 0)
                    break;

                ulong regionBase = (ulong)info.BaseAddress.ToInt64();
                ulong regionSize = info.RegionSize.ToUInt64();
                uint basicProtection = info.Protect & 0xFF;
                bool writable = basicProtection == 0x04 || basicProtection == 0x08 ||
                    basicProtection == 0x40 || basicProtection == 0x80;

                if (info.State == MemCommit && info.Type == MemPrivate && writable &&
                    (info.Protect & PageGuard) == 0 &&
                    (info.Protect & PageNoAccess) == 0 &&
                    regionSize >= (ulong)recordSize)
                {
                    ulong offset = 0;
                    while (offset < regionSize && !token.IsCancellationRequested)
                    {
                        ulong remaining = regionSize - offset;
                        int wanted = (int)Math.Min((ulong)ChunkSize, remaining);
                        byte[] data = new byte[wanted];
                        UIntPtr bytesRead;
                        if (ReadProcessMemory(
                                game.Handle,
                                new IntPtr((long)(regionBase + offset)),
                                data,
                                new UIntPtr((uint)wanted),
                                out bytesRead))
                        {
                            int count = (int)Math.Min((ulong)wanted, bytesRead.ToUInt64());
                            for (int i = 0; i <= count - recordSize; i += 8)
                            {
                                if (isMatch(data, i))
                                    return new IntPtr((long)(regionBase + offset + (ulong)i));
                            }
                        }

                        if (remaining <= ChunkSize)
                            break;
                        offset += (ulong)(ChunkSize - recordSize);
                    }
                }

                if (regionSize == 0 || regionBase > ulong.MaxValue - regionSize)
                    break;
                address = regionBase + regionSize;
            }

            return IntPtr.Zero;
        }

        private static bool IsLevelLoaderMembers(byte[] data, int offset)
        {
            if (BitConverter.ToInt64(data, offset + 0x08) !=
                BitConverter.DoubleToInt64Bits(32065.0))
                return false;

            int[] types = { 3, 1, 28, -1, 1, 28, 2, 2, 1, 2, -1 };
            for (int member = 0; member < types.Length; member++)
            {
                int variant = offset + member * VariantSize;
                if ((types[member] < 0 && !IsNilOrObject(data, variant)) ||
                    (types[member] >= 0 && !HasType(data, variant, types[member])))
                    return false;
            }

            long batchTotal = BitConverter.ToInt64(data, offset + 0x98);
            long batchDone = BitConverter.ToInt64(data, offset + 0xB0);
            long currentLevel = BitConverter.ToInt64(data, offset + 0xE0);
            return IsBoolean(data, offset + 0x20) &&
                IsBoolean(data, offset + 0x68) &&
                IsBoolean(data, offset + 0xC8) &&
                batchTotal >= 0 && batchTotal < 1000 &&
                batchDone >= 0 && batchDone < 1000 &&
                currentLevel >= 0 && currentLevel <= 7;
        }

        private static bool IsPlayerMembers(byte[] data, int offset)
        {
            int[] members = { 0, 14, 16, 18, 19, 20, 57, 58 };
            int[] types = { 2, 3, 1, 3, 9, 1, 24, 1 };
            for (int index = 0; index < members.Length; index++)
            {
                if (!HasType(
                        data,
                        offset + members[index] * VariantSize,
                        types[index]))
                    return false;
            }

            int[] nilMembers = { 1, 15, 17 };
            for (int index = 0; index < nilMembers.Length; index++)
            {
                if (!HasType(data, offset + nilMembers[index] * VariantSize, 0))
                    return false;
            }

            for (int member = 2; member <= 13; member++)
            {
                if (!HasType(data, offset + member * VariantSize, 24))
                    return false;
            }

            for (int member = 21; member <= 57; member++)
            {
                if (!HasType(data, offset + member * VariantSize, 24))
                    return false;
            }

            return IsBoolean(data, offset + 16 * VariantSize + 8) &&
                IsBoolean(data, offset + 20 * VariantSize + 8) &&
                IsBoolean(data, offset + 58 * VariantSize + 8) &&
                IsPlausiblePointer(BitConverter.ToInt64(
                    data,
                    offset + PlayerCurrentBodyMember * VariantSize + 16));
        }

        private static bool IsMainCutsceneStarterMembers(byte[] data, int offset)
        {
            if (BitConverter.ToInt64(data, offset + 0x98) !=
                BitConverter.DoubleToInt64Bits(3.0))
                return false;

            int[] types = { 24, 1, 1, 1, 3, 3, 3, 3, 3, -1 };
            for (int member = 0; member < types.Length; member++)
            {
                int variant = offset + member * VariantSize;
                if ((types[member] < 0 && !IsNilOrObject(data, variant)) ||
                    (types[member] >= 0 && !HasType(data, variant, types[member])))
                    return false;
            }

            return BitConverter.ToInt64(data, offset + 0x10) != 0 &&
                BitConverter.ToInt64(data, offset + 0x20) == 0 &&
                BitConverter.ToInt64(data, offset + 0x38) == 1 &&
                BitConverter.ToInt64(data, offset + 0x50) == 1 &&
                BitConverter.ToInt64(data, offset + 0x68) == BitConverter.DoubleToInt64Bits(1.0) &&
                BitConverter.ToInt64(data, offset + 0x80) == 0 &&
                BitConverter.ToInt64(data, offset + 0xB0) == 0 &&
                BitConverter.ToInt64(data, offset + 0xC8) == BitConverter.DoubleToInt64Bits(-80.0);
        }

        private static bool HasType(byte[] data, int offset, int expected)
        {
            return BitConverter.ToInt64(data, offset) == expected;
        }

        private static bool IsNilOrObject(byte[] data, int offset)
        {
            long type = BitConverter.ToInt64(data, offset);
            return type == 0 || type == 24;
        }

        private static bool IsBoolean(byte[] data, int offset)
        {
            long value = BitConverter.ToInt64(data, offset);
            return value == 0 || value == 1;
        }

        private static bool IsPlausiblePointer(long value)
        {
            return IsPlausibleAddress(value) && (value & 7) == 0;
        }

        private static bool IsPlausibleAddress(long value)
        {
            return value > 0x10000 && value < MaximumUserAddress;
        }

        private static bool Is64Bit(Process candidate)
        {
            if (!Environment.Is64BitOperatingSystem)
                return false;
            bool isWow64;
            return IsWow64Process(candidate.Handle, out isWow64) && !isWow64;
        }

        private void Detach()
        {
            levelScanCancellation?.Cancel();
            levelScanCancellation?.Dispose();
            levelScanCancellation = null;
            levelScanTask = null;
            levelLoaderMembers = IntPtr.Zero;
            ClearPlayerTracking();
            cutsceneStarterMembers = IntPtr.Zero;
            previousLevel = null;
            cutsceneRequestWasActive = false;

            if (process != null)
            {
                int processId = -1;
                try { processId = process.Id; } catch { }
                process.Dispose();
                process = null;
                SNAPTICDemoLog.Write("Detached from snaptic.exe" +
                    (processId >= 0 ? " PID " + processId : string.Empty) + ".");
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Detach();
        }

        private static string FormatPosition(Position position)
        {
            return "(" +
                position.X.ToString("0.000") + ", " +
                position.Y.ToString("0.000") + ", " +
                position.Z.ToString("0.000") + ")";
        }

        private struct Position
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Z;

            public Position(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool IsPlausible =>
                !float.IsNaN(X) && !float.IsInfinity(X) && Math.Abs(X) < 10000.0f &&
                !float.IsNaN(Y) && !float.IsInfinity(Y) && Math.Abs(Y) < 10000.0f &&
                !float.IsNaN(Z) && !float.IsInfinity(Z) && Math.Abs(Z) < 10000.0f;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int VirtualQueryEx(
            IntPtr process,
            IntPtr address,
            out MemoryBasicInformation information,
            UIntPtr length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr process,
            IntPtr address,
            byte[] buffer,
            UIntPtr size,
            out UIntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr process, out bool isWow64);
    }
}
