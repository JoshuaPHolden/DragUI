using BepInEx.Logging;
using ProjectM;
using ProjectM.Scripting;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace VBloodHunter;

// Client-world locator. Everything here is bound to ONE client World: leaving a game or switching
// servers disposes that World, so Reset() (called from ClientBootstrapSystem.OnDestroy) drops every
// reference with plain field writes. Never touch ECS/UI from the teardown hook itself.
internal static class Core
{
    static World _client;
    static ClientScriptMapper _clientScriptMapper;
    static Entity _localCharacter = Entity.Null;
    static Entity _localUser = Entity.Null;

    public static ManualLogSource Log => Plugin.LogInstance;

    public static bool IsReady => _client != null && _client.IsCreated;
    public static World Client => _client;
    public static EntityManager EntityManager => _client.EntityManager;

    public static ClientScriptMapper ClientScriptMapper =>
        _clientScriptMapper ??= _client.GetExistingSystemManaged<ClientScriptMapper>();

    static bool _warnedBadServerTime;

    /// <summary>Server-synchronised game time in seconds (the clock VbloodGhostBuff_Shared.DestroyTime and
    /// Health.TimeOfDeath use). False until the client has a sane value.</summary>
    public static bool TryGetServerTime(out double serverTime)
    {
        serverTime = 0;
        try
        {
            serverTime = ClientScriptMapper._ClientGameManager.ServerTime.Time;
        }
        catch (Exception ex)
        {
            if (!_warnedBadServerTime) Log.LogWarning($"Couldn't read server time: {ex.Message}");
            _warnedBadServerTime = true;
            return false;
        }

        if (double.IsFinite(serverTime) && serverTime > 0) return true;
        if (!_warnedBadServerTime) Log.LogWarning($"Server time not usable yet ({serverTime}); exact timers wait for it.");
        _warnedBadServerTime = true;
        return false;
    }

    /// <summary>Convert a server timestamp to the local countdown clock (VBloodTracker.Now).</summary>
    public static bool TryServerToLocal(double serverTimestamp, out double local)
    {
        local = 0;
        if (!double.IsFinite(serverTimestamp) || serverTimestamp <= 0 || !TryGetServerTime(out double serverNow)) return false;
        local = Services.VBloodTracker.Now + (serverTimestamp - serverNow);
        return double.IsFinite(local);
    }

    // Never hand a null/disposed World to native code: that crashes the client outright.
    public static Entity LocalCharacter =>
        !IsReady ? Entity.Null
        : _localCharacter.Exists() ? _localCharacter
        : ConsoleShared.TryGetLocalCharacterInCurrentWorld(out _localCharacter, _client) ? _localCharacter : Entity.Null;

    public static Entity LocalUser =>
        !IsReady ? Entity.Null
        : _localUser.Exists() ? _localUser
        : ConsoleShared.TryGetLocalUserInCurrentWorld(out _localUser, _client) ? _localUser : Entity.Null;

    /// <summary>True once we're in a world AND the local character exists (i.e. actually in game).</summary>
    public static bool InGame => IsReady && LocalCharacter != Entity.Null;

    /// <summary>Same native World? (IL2CPP hands out a new wrapper object per call, so compare pointers.)</summary>
    public static bool IsBoundTo(World world) => _client != null && world != null && _client.Pointer == world.Pointer;

    public static void Initialize(World world)
    {
        if (IsBoundTo(world)) return;
        Reset();
        _client = world;
        Log.LogInfo($"Client world bound ({world.Name}).");
    }

    public static void Reset()
    {
        _client = null;
        _clientScriptMapper = null;
        _warnedBadServerTime = false;
        _localCharacter = Entity.Null;
        _localUser = Entity.Null;
    }

    public static bool TryGetLocalPosition(out float2 position)
    {
        position = default;
        var character = LocalCharacter;
        if (character == Entity.Null || !character.Has<LocalToWorld>()) return false;
        var p = character.Read<LocalToWorld>().Position;
        position = new float2(p.x, p.z);
        return true;
    }

    public static void Diag(string message)
    {
        if (Settings.Diagnostics) Log.LogInfo($"[diag] {message}");
    }
}

internal static class EntityExtensions
{
    static EntityManager EM => Core.EntityManager;

    public static bool Exists(this Entity entity) =>
        entity != Entity.Null && Core.IsReady && EM.Exists(entity);

    public static bool Has<T>(this Entity entity) =>
        EM.HasComponent<T>(entity);

    public static T Read<T>(this Entity entity) where T : struct =>
        EM.TryGetComponentData<T>(entity, out T data) ? data : default;

    public static void Write<T>(this Entity entity, T data) where T : struct =>
        EM.SetComponentData(entity, data);
}
