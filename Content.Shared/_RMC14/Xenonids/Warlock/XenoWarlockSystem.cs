using System.Numerics;
using Content.Shared._RMC14.Dialog;
using Content.Shared._RMC14.Line;
using Content.Shared._RMC14.Map;
using Content.Shared._RMC14.Slow;
using Content.Shared._RMC14.Stamina;
using Content.Shared._RMC14.Stun;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.ManageHive.Boons;
using Content.Shared._RMC14.Xenonids.Plasma;
using Content.Shared.Coordinates;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Xenonids.Warlock;

public sealed class XenoWarlockSystem : EntitySystem
{
    private static readonly DamageSpecifier BlastDirectDamage = new() { DamageDict = { ["Heat"] = 35, }, };
    private static readonly DamageSpecifier BlastDrainDirectDamage = new() { DamageDict = { ["Stamina"] = 24, }, };
    private static readonly DamageSpecifier BlastDrainAreaDamage = new() { DamageDict = { ["Stamina"] = 31, }, };
    private static readonly DamageSpecifier LanceDamage = new() { DamageDict = { ["Heat"] = 60, }, };
    private static readonly DamageSpecifier CrushDamage = new() { DamageDict = { ["Blunt"] = 50, }, };
    private static readonly DamageSpecifier ShieldDetonateDamage = new() { DamageDict = { ["Blunt"] = 25, }, };

    private static readonly EntProtoId<XenoWarlockShieldComponent> ShieldPrototype = "XenoWarlockPsychicShield";
    private static readonly EntProtoId BlastChargeEffectPrototype = "RMCEffectWarlockBlastCharge";
    private static readonly EntProtoId CrushWarningEffectPrototype = "RMCEffectWarlockCrushWarning";
    private static readonly EntProtoId CrushOrbEffectPrototype = "RMCEffectWarlockCrushOrb";
    private static readonly EntProtoId CrushHardEffectPrototype = "RMCEffectWarlockCrushHard";
    private static readonly EntProtoId CrushSmoothEffectPrototype = "RMCEffectWarlockCrushSmooth";

    private const float BlastCastTimeSeconds = 1f;
    private const float ShieldDurationSeconds = 8f;
    private const float ShieldOffset = 1.1f;
    private const float CrushExpandSeconds = 0.6f;
    private const int CrushMaxIterations = 5;

    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly DialogSystem _dialog = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookup = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly HiveBoonSystem _hiveBoons = default!;
    [Dependency] private readonly LineSystem _line = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly RMCMapSystem _rmcMap = default!;
    [Dependency] private readonly RMCSizeStunSystem _sizeStun = default!;
    [Dependency] private readonly RMCSlowSystem _slow = default!;
    [Dependency] private readonly RMCStaminaSystem _rmcStamina = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly XenoPlasmaSystem _plasma = default!;
    [Dependency] private readonly XenoSystem _xeno = default!;

    private readonly HashSet<EntityUid> _targets = new();
    private readonly HashSet<EntityUid> _lineHits = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoWarlockComponent, XenoPsychicBlastActionEvent>(OnPsychicBlast);
        SubscribeLocalEvent<XenoWarlockComponent, XenoPsychicBlastDoAfterEvent>(OnPsychicBlastDoAfter);
        SubscribeLocalEvent<XenoWarlockComponent, XenoPsychicCrushActionEvent>(OnPsychicCrush);
        SubscribeLocalEvent<XenoWarlockComponent, XenoPsychicShieldActionEvent>(OnPsychicShield);
        SubscribeLocalEvent<XenoWarlockComponent, XenoPsyDrainActionEvent>(OnPsyDrain);
        SubscribeLocalEvent<XenoWarlockComponent, XenoWarlockMutationsActionEvent>(OnMutationsAction);
        SubscribeLocalEvent<XenoWarlockComponent, XenoWarlockToggleBlastModeActionEvent>(OnToggleBlastMode);
        SubscribeLocalEvent<XenoWarlockComponent, XenoWarlockChooseMutationEvent>(OnChooseMutation);
        SubscribeLocalEvent<XenoWarlockComponent, XenoWarlockChooseModeEvent>(OnChooseMode);
        SubscribeLocalEvent<XenoWarlockComponent, XenoPsyDrainDoAfterEvent>(OnPsyDrainDoAfter);
        SubscribeLocalEvent<XenoWarlockPrimordialComponent, ComponentStartup>(OnPrimordialStartup);
        SubscribeLocalEvent<XenoWarlockPrimordialComponent, ComponentShutdown>(OnPrimordialShutdown);
        SubscribeLocalEvent<XenoWarlockShieldComponent, ProjectileReflectAttemptEvent>(OnShieldProjectileReflectAttempt);
        SubscribeLocalEvent<XenoWarlockShieldComponent, HitScanReflectAttemptEvent>(OnShieldHitscanReflectAttempt);
        SubscribeLocalEvent<XenoWarlockShieldComponent, EntityTerminatingEvent>(OnShieldTerminating);
        SubscribeLocalEvent<HiveBoonBuildMutationChamberEvent>(OnBuildMutationChamber);
    }

    private void OnPrimordialStartup(Entity<XenoWarlockPrimordialComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp(ent, out XenoWarlockComponent? warlock))
            return;

        warlock.Primordial = true;
        Dirty(ent, warlock);
        _popup.PopupEntity(Loc.GetString("rmc-warlock-primordial-popup"), ent, ent);
    }

    private void OnPrimordialShutdown(Entity<XenoWarlockPrimordialComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp(ent, out XenoWarlockComponent? warlock))
            return;

        warlock.Primordial = false;
        if (warlock.BlastMode == XenoWarlockBlastMode.PsychicLance)
            warlock.BlastMode = XenoWarlockBlastMode.PsychicBlast;

        Dirty(ent, warlock);
    }

    private void OnToggleBlastMode(Entity<XenoWarlockComponent> ent, ref XenoWarlockToggleBlastModeActionEvent args)
    {
        args.Handled = true;

        var next = ent.Comp.BlastMode switch
        {
            XenoWarlockBlastMode.PsychicBlast when ent.Comp.DrainingBlast => XenoWarlockBlastMode.PsychicDrain,
            XenoWarlockBlastMode.PsychicBlast when ent.Comp.Primordial => XenoWarlockBlastMode.PsychicLance,
            XenoWarlockBlastMode.PsychicDrain when ent.Comp.Primordial => XenoWarlockBlastMode.PsychicLance,
            _ => XenoWarlockBlastMode.PsychicBlast,
        };

        if (next == XenoWarlockBlastMode.PsychicDrain && !ent.Comp.DrainingBlast)
            next = ent.Comp.Primordial ? XenoWarlockBlastMode.PsychicLance : XenoWarlockBlastMode.PsychicBlast;

        if (next == XenoWarlockBlastMode.PsychicLance && !ent.Comp.Primordial)
            next = XenoWarlockBlastMode.PsychicBlast;

        ent.Comp.BlastMode = next;
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("rmc-warlock-mode-popup", ("mode", GetModeName(next))), ent, ent);
    }

    private void OnChooseMode(Entity<XenoWarlockComponent> ent, ref XenoWarlockChooseModeEvent args)
    {
        if (!CanUseMode(ent.Comp, args.Mode))
            return;

        ent.Comp.BlastMode = args.Mode;
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("rmc-warlock-mode-popup", ("mode", GetModeName(args.Mode))), ent, ent);
    }

    private void OnMutationsAction(Entity<XenoWarlockComponent> ent, ref XenoWarlockMutationsActionEvent args)
    {
        args.Handled = true;

        var options = new List<DialogOption>();
        if (!ent.Comp.CautiousMind && GetMutationChamberCount(ent.Owner, XenoWarlockMutationCategory.Shell) > 0)
            options.Add(new DialogOption(Loc.GetString("rmc-warlock-mutation-cautious"), new XenoWarlockChooseMutationEvent(XenoWarlockMutationCategory.Shell)));
        if (!ent.Comp.DrainingBlast && GetMutationChamberCount(ent.Owner, XenoWarlockMutationCategory.Spur) > 0)
            options.Add(new DialogOption(Loc.GetString("rmc-warlock-mutation-draining"), new XenoWarlockChooseMutationEvent(XenoWarlockMutationCategory.Spur)));
        if (!ent.Comp.MobileMind && GetMutationChamberCount(ent.Owner, XenoWarlockMutationCategory.Veil) > 0)
            options.Add(new DialogOption(Loc.GetString("rmc-warlock-mutation-mobile"), new XenoWarlockChooseMutationEvent(XenoWarlockMutationCategory.Veil)));

        options.Add(new DialogOption(Loc.GetString("rmc-warlock-mode-blast"), new XenoWarlockChooseModeEvent(XenoWarlockBlastMode.PsychicBlast)));
        if (ent.Comp.DrainingBlast)
            options.Add(new DialogOption(Loc.GetString("rmc-warlock-mode-drain"), new XenoWarlockChooseModeEvent(XenoWarlockBlastMode.PsychicDrain)));
        if (ent.Comp.Primordial)
            options.Add(new DialogOption(Loc.GetString("rmc-warlock-mode-lance"), new XenoWarlockChooseModeEvent(XenoWarlockBlastMode.PsychicLance)));

        if (_hive.GetHive(ent.Owner) is not { } hive)
            return;

        var boons = _hiveBoons.EnsureBoons(hive).Comp;
        var message = Loc.GetString("rmc-warlock-mutations-message",
            ("shell", GetMutationChamberCount(ent.Owner, XenoWarlockMutationCategory.Shell)),
            ("spur", GetMutationChamberCount(ent.Owner, XenoWarlockMutationCategory.Spur)),
            ("veil", GetMutationChamberCount(ent.Owner, XenoWarlockMutationCategory.Veil)),
            ("psy", boons.PsyPoints),
            ("mode", GetModeName(ent.Comp.BlastMode)),
            ("primordial", ent.Comp.Primordial ? Loc.GetString("rmc-warlock-yes") : Loc.GetString("rmc-warlock-no")));

        _dialog.OpenOptions(ent, Loc.GetString("rmc-warlock-mutations-title"), options, message);
    }

    private void OnChooseMutation(Entity<XenoWarlockComponent> ent, ref XenoWarlockChooseMutationEvent args)
    {
        if (GetMutationChamberCount(ent.Owner, args.Category) <= 0)
            return;

        switch (args.Category)
        {
            case XenoWarlockMutationCategory.Shell:
                ent.Comp.CautiousMind = true;
                _popup.PopupEntity(Loc.GetString("rmc-warlock-cautious-popup"), ent, ent);
                break;
            case XenoWarlockMutationCategory.Spur:
                ent.Comp.DrainingBlast = true;
                _popup.PopupEntity(Loc.GetString("rmc-warlock-draining-popup"), ent, ent);
                break;
            case XenoWarlockMutationCategory.Veil:
                ent.Comp.MobileMind = true;
                _popup.PopupEntity(Loc.GetString("rmc-warlock-mobile-popup"), ent, ent);
                break;
        }

        Dirty(ent);
    }

    private void OnPsyDrain(Entity<XenoWarlockComponent> ent, ref XenoPsyDrainActionEvent args)
    {
        if (args.Handled)
            return;

        if (!CanDrainCorpse(ent, args.Target))
            return;

        args.Handled = true;
        var doAfter = new DoAfterArgs(EntityManager, ent, TimeSpan.FromSeconds(5), new XenoPsyDrainDoAfterEvent(), ent, args.Target)
        {
            BreakOnMove = true,
            CancelDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameTool,
        };

        _popup.PopupEntity(Loc.GetString("rmc-warlock-psydrain-start", ("target", args.Target)), ent, ent);
        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnPsyDrainDoAfter(Entity<XenoWarlockComponent> ent, ref XenoPsyDrainDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        if (!CanDrainCorpse(ent, target))
            return;

        args.Handled = true;
        EnsureComp<PsyDrainedCorpseComponent>(target);

        if (_hive.GetHive(ent.Owner) is { } hive)
        {
            var boons = _hiveBoons.EnsureBoons(hive);
            boons.Comp.PsyPoints = Math.Clamp(boons.Comp.PsyPoints + 2, 0, boons.Comp.PsyPointsMax);
            Dirty(boons);
            _hive.IncreaseBurrowedLarva(hive, 1);
        }

        _popup.PopupEntity(Loc.GetString("rmc-warlock-psydrain-finish", ("target", target)), ent, ent);
    }

    private bool CanDrainCorpse(Entity<XenoWarlockComponent> ent, EntityUid target)
    {
        if (HasComp<PsyDrainedCorpseComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("rmc-warlock-psydrain-already"), ent, ent);
            return false;
        }

        if (!_mobState.IsDead(target) || !HasComp<HumanoidAppearanceComponent>(target) || HasComp<Content.Shared._RMC14.Synth.SynthComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("rmc-warlock-psydrain-invalid"), ent, ent);
            return false;
        }

        return true;
    }

    private void OnPsychicBlast(Entity<XenoWarlockComponent> ent, ref XenoPsychicBlastActionEvent args)
    {
        if (args.Handled)
            return;

        if (!CanUseMode(ent.Comp, ent.Comp.BlastMode))
        {
            ent.Comp.BlastMode = XenoWarlockBlastMode.PsychicBlast;
            Dirty(ent);
        }

        var source = _transform.GetMapCoordinates(ent);
        var target = _transform.ToMapCoordinates(args.Target);
        if (source.MapId != target.MapId)
            return;

        var direction = target.Position - source.Position;
        if (direction.LengthSquared() <= 0.01f)
            return;

        var range = GetModeRange(ent.Comp.BlastMode);
        if (direction.Length() > range)
            target = source.Offset(direction.Normalized() * range);

        args.Handled = true;
        CleanupBlastCharge(ent);
        ent.Comp.BlastChargeEffect = Spawn(BlastChargeEffectPrototype, ent.Owner.ToCoordinates());

        var doAfter = new DoAfterArgs(EntityManager,
            ent,
            TimeSpan.FromSeconds(BlastCastTimeSeconds),
            new XenoPsychicBlastDoAfterEvent(GetNetCoordinates(_transform.ToCoordinates(target)), ent.Comp.BlastMode),
            ent,
            args.Action)
        {
            BreakOnMove = true,
            CancelDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnPsychicBlastDoAfter(Entity<XenoWarlockComponent> ent, ref XenoPsychicBlastDoAfterEvent args)
    {
        CleanupBlastCharge(ent);

        if (args.Cancelled || args.Handled)
            return;

        var target = GetCoordinates(args.Location).ToMap(EntityManager, _transform);
        var source = _transform.GetMapCoordinates(ent);
        if (source.MapId != target.MapId)
            return;

        if (!_plasma.TryRemovePlasmaPopup(ent.Owner, GetModeCost(args.Mode)))
            return;

        args.Handled = true;
        switch (args.Mode)
        {
            case XenoWarlockBlastMode.PsychicDrain:
                DoDrainBlast(ent, source, target);
                break;
            case XenoWarlockBlastMode.PsychicLance:
                DoLance(ent, source, target);
                break;
            default:
                DoBlast(ent, source, target);
                break;
        }
    }

    private void CleanupBlastCharge(Entity<XenoWarlockComponent> ent)
    {
        if (ent.Comp.BlastChargeEffect is { } effect && EntityManager.EntityExists(effect))
            QueueDel(effect);

        ent.Comp.BlastChargeEffect = null;
    }

    private void DoBlast(Entity<XenoWarlockComponent> ent, MapCoordinates source, MapCoordinates target)
    {
        FindFirstTarget(ent, source, target, 7f, out var hit);
        if (hit is not { } direct)
        {
            _audio.PlayPredicted(new SoundPathSpecifier("/Audio/_RMC14/Xeno/blobattack.ogg"), ent, ent);
            return;
        }

        _damageable.TryChangeDamage(direct, BlastDirectDamage, armorPiercing: 10, origin: ent, tool: ent);
        _stun.TryParalyze(direct, TimeSpan.FromSeconds(0.5f), true);
        _slow.TrySlowdown(direct, TimeSpan.FromSeconds(2), true);
        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/_RMC14/Xeno/blobattack.ogg"), ent, ent);
    }

    private void DoDrainBlast(Entity<XenoWarlockComponent> ent, MapCoordinates source, MapCoordinates target)
    {
        var impact = FindFirstTarget(ent, source, target, 7f, out var hit) ?? target;
        if (hit is { } direct)
        {
            _damageable.TryChangeDamage(direct, BlastDrainDirectDamage, armorPiercing: 10, origin: ent, tool: ent);
            if (TryComp<RMCStaminaComponent>(direct, out _))
                _sizeStun.TryKnockOut(direct, TimeSpan.FromSeconds(1));
        }

        _targets.Clear();
        _entityLookup.GetEntitiesInRange(impact.MapId, impact.Position, 1.5f, _targets);
        foreach (var uid in _targets)
        {
            if (!_xeno.CanAbilityAttackTarget(ent, uid))
                continue;

            _damageable.TryChangeDamage(uid, BlastDrainAreaDamage, armorPiercing: 5, origin: ent, tool: ent);
            _slow.TrySlowdown(uid, TimeSpan.FromSeconds(2), true);
            _sizeStun.KnockBack(uid, impact, 1.5f, 1.5f, 8f, ignoreSize: false);
        }

        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/_RMC14/Xeno/blobattack.ogg"), ent, ent);
    }

    private void DoLance(Entity<XenoWarlockComponent> ent, MapCoordinates source, MapCoordinates target)
    {
        _lineHits.Clear();
        var tiles = _line.DrawLine(_transform.ToCoordinates(source), _transform.ToCoordinates(target), TimeSpan.Zero, 9f, out _, ignoreBarricades: true);
        foreach (var tile in tiles)
        {
            _targets.Clear();
            _entityLookup.GetEntitiesInRange(tile.Coordinates.MapId, tile.Coordinates.Position, 0.45f, _targets, LookupFlags.Uncontained);
            foreach (var uid in _targets)
            {
                if (!_lineHits.Add(uid))
                    continue;

                if (_xeno.CanAbilityAttackTarget(ent, uid))
                {
                    _damageable.TryChangeDamage(uid, LanceDamage, armorPiercing: 50, origin: ent, tool: ent);
                    _slow.TrySlowdown(uid, TimeSpan.FromSeconds(2), true);
                    _sizeStun.KnockBack(uid, source, 1f, 1f, 7f, ignoreSize: false);
                }
            }
        }

        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/_RMC14/Xeno/blobattack.ogg"), ent, ent);
    }

    private void OnPsychicCrush(Entity<XenoWarlockComponent> ent, ref XenoPsychicCrushActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (ent.Comp.ActiveCrush != null)
        {
            ResolveCrush(ent);
            return;
        }

        var source = _transform.GetMapCoordinates(ent);
        var target = _transform.ToMapCoordinates(args.Target);
        if (source.MapId != target.MapId)
            return;

        var direction = target.Position - source.Position;
        if (direction.LengthSquared() <= 0.01f)
            return;

        if (direction.Length() > 7f)
            target = source.Offset(direction.Normalized() * 7f);

        if (!TryStartCrush(ent, target))
            return;

        _popup.PopupEntity(Loc.GetString("rmc-warlock-crush-start"), ent, ent);
    }

    private bool TryStartCrush(Entity<XenoWarlockComponent> ent, MapCoordinates target)
    {
        var targetCoords = _transform.ToCoordinates(target);
        if (!_rmcMap.TryGetTileRefForEnt(targetCoords, out var grid, out var tile))
            return false;

        var center = _turf.GetTileCenter(tile);
        ent.Comp.ActiveCrush = _transform.ToMapCoordinates(center);
        ent.Comp.CrushGrid = grid.Owner;
        ent.Comp.CrushStart = _timing.CurTime;
        ent.Comp.NextCrushExpandAt = _timing.CurTime + TimeSpan.FromSeconds(CrushExpandSeconds);
        ent.Comp.CrushIterations = 1;
        ent.Comp.CrushTiles.Clear();
        ent.Comp.CrushFrontier.Clear();
        ent.Comp.CrushWarnings.Clear();
        ent.Comp.CrushTiles.Add(tile.GridIndices);
        ent.Comp.CrushFrontier.Add(tile.GridIndices);
        ent.Comp.CrushWarnings.Add(Spawn(CrushWarningEffectPrototype, center));
        ent.Comp.CrushOrb = Spawn(CrushOrbEffectPrototype, center);
        Dirty(ent);
        return true;
    }

    private void ExpandCrush(Entity<XenoWarlockComponent> ent)
    {
        if (ent.Comp.ActiveCrush == null ||
            ent.Comp.CrushGrid is not { } gridId ||
            !TryComp(gridId, out MapGridComponent? grid))
        {
            StopCrush(ent, false);
            return;
        }

        if (ent.Comp.CrushIterations >= CrushMaxIterations)
        {
            ResolveCrush(ent);
            return;
        }

        var nextFrontier = new HashSet<Vector2i>();
        foreach (var tile in ent.Comp.CrushFrontier)
        {
            foreach (var direction in _rmcMap.CardinalDirections)
            {
                var neighbor = tile.Offset(direction);
                if (ent.Comp.CrushTiles.Contains(neighbor))
                    continue;

                if (!_map.TryGetTileRef(gridId, grid, neighbor, out var tileRef) || tileRef.Tile.IsEmpty)
                    continue;

                var coordinates = _map.GridTileToLocal(gridId, grid, neighbor);
                if (_rmcMap.IsTileBlocked(coordinates, CollisionGroup.Impassable))
                    continue;

                ent.Comp.CrushTiles.Add(neighbor);
                nextFrontier.Add(neighbor);
                ent.Comp.CrushWarnings.Add(Spawn(CrushWarningEffectPrototype, _turf.GetTileCenter(tileRef)));
            }
        }

        if (nextFrontier.Count == 0)
        {
            ResolveCrush(ent);
            return;
        }

        ent.Comp.CrushFrontier = nextFrontier;
        ent.Comp.CrushIterations++;
        ent.Comp.NextCrushExpandAt = _timing.CurTime + TimeSpan.FromSeconds(CrushExpandSeconds);
        Dirty(ent);
    }

    private void ResolveCrush(Entity<XenoWarlockComponent> ent)
    {
        if (ent.Comp.ActiveCrush is not { } center ||
            ent.Comp.CrushGrid is not { } gridId ||
            !TryComp(gridId, out MapGridComponent? grid))
        {
            StopCrush(ent, false);
            return;
        }

        var cost = FixedPoint2.New(100 + ent.Comp.CrushIterations * 35);
        if (_mobState.IsIncapacitated(ent) || !_plasma.TryRemovePlasmaPopup(ent.Owner, cost))
        {
            StopCrush(ent, false);
            return;
        }

        foreach (var tile in ent.Comp.CrushTiles)
        {
            if (!_map.TryGetTileRef(gridId, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;

            var tileCenter = _turf.GetTileCenter(tileRef);
            _targets.Clear();
            _entityLookup.GetEntitiesInRange(tileCenter, 0.6f, _targets, LookupFlags.Uncontained);
            foreach (var uid in _targets)
            {
                if (!_xeno.CanAbilityAttackTarget(ent, uid))
                    continue;

                _damageable.TryChangeDamage(uid, CrushDamage, armorPiercing: 15, origin: ent, tool: ent);
                if (TryComp<RMCStaminaComponent>(uid, out _))
                    _rmcStamina.DoStaminaDamage(uid, 75);

                _stun.TryParalyze(uid, TimeSpan.FromSeconds(1), true);
                _slow.TrySuperSlowdown(uid, TimeSpan.FromSeconds(4), true);
            }
        }

        Spawn(CrushHardEffectPrototype, center);
        StopCrush(ent, true);
        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/_RMC14/Xeno/blobattack.ogg"), ent, ent);
    }

    private void StopCrush(Entity<XenoWarlockComponent> ent, bool resolved)
    {
        if (ent.Comp.CrushOrb is { } orb && EntityManager.EntityExists(orb))
            QueueDel(orb);

        foreach (var warning in ent.Comp.CrushWarnings)
        {
            if (EntityManager.EntityExists(warning))
                QueueDel(warning);
        }

        if (!resolved && ent.Comp.ActiveCrush is { } center)
            Spawn(CrushSmoothEffectPrototype, center);

        ent.Comp.ActiveCrush = null;
        ent.Comp.CrushGrid = null;
        ent.Comp.CrushOrb = null;
        ent.Comp.CrushIterations = 0;
        ent.Comp.CrushTiles.Clear();
        ent.Comp.CrushFrontier.Clear();
        ent.Comp.CrushWarnings.Clear();
        Dirty(ent);
    }

    private void OnPsychicShield(Entity<XenoWarlockComponent> ent, ref XenoPsychicShieldActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (ent.Comp.ActiveShield is { } shield && EntityManager.EntityExists(shield))
        {
            DetonateShield(ent, shield, false);
            return;
        }

        var source = _transform.GetMapCoordinates(ent);
        var target = _transform.ToMapCoordinates(args.Target);
        if (source.MapId != target.MapId)
            return;

        var delta = target.Position - source.Position;
        if (delta.LengthSquared() <= 0.01f)
            return;

        if (!_plasma.TryRemovePlasmaPopup(ent.Owner, FixedPoint2.New(120)))
            return;

        var cardinal = delta.ToAngle().GetCardinalDir();
        var rotation = cardinal.ToAngle();
        var shieldEnt = Spawn(ShieldPrototype, new EntityCoordinates(ent, cardinal.ToVec() * ShieldOffset));
        _hive.SetSameHive(ent.Owner, shieldEnt);
        _transform.SetLocalRotation(shieldEnt, rotation);

        var shieldComp = Comp<XenoWarlockShieldComponent>(shieldEnt);
        shieldComp.Warlock = ent.Owner;
        shieldComp.Direction = rotation;
        shieldComp.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(ShieldDurationSeconds);
        Dirty(shieldEnt, shieldComp);

        ent.Comp.ActiveShield = shieldEnt;
        Dirty(ent);
        _slow.TryRoot(ent, TimeSpan.FromSeconds(ShieldDurationSeconds));
    }

    private void OnShieldProjectileReflectAttempt(Entity<XenoWarlockShieldComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (TryFreezeProjectile(ent, args.ProjUid))
            args.Cancelled = true;
    }

    private void OnShieldHitscanReflectAttempt(Entity<XenoWarlockShieldComponent> ent, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected)
            return;

        var normal = ent.Comp.Direction.ToVec().Normalized();
        if (Vector2.Dot(args.Direction.Normalized(), normal) > -0.15f)
            return;

        args.Direction = ReflectDirection(args.Direction, normal);
        args.Reflected = true;
    }

    private bool TryFreezeProjectile(Entity<XenoWarlockShieldComponent> shield, EntityUid projectile)
    {
        if (!TryComp(projectile, out PhysicsComponent? physics) ||
            !TryComp(projectile, out ProjectileComponent? projectileComp))
            return false;

        foreach (var frozen in shield.Comp.FrozenProjectiles)
        {
            if (frozen.Projectile == projectile)
                return true;
        }

        var mapVelocity = _physics.GetMapLinearVelocity(projectile, component: physics);
        if (mapVelocity.LengthSquared() <= 0.01f)
            return false;

        var normal = shield.Comp.Direction.ToVec().Normalized();
        if (Vector2.Dot(mapVelocity.Normalized(), normal) > -0.15f)
            return false;

        shield.Comp.FrozenProjectiles.Add(new XenoWarlockFrozenProjectile
        {
            Projectile = projectile,
            LinearVelocity = physics.LinearVelocity,
            Rotation = Transform(projectile).LocalRotation,
            Shooter = projectileComp.Shooter,
            Weapon = projectileComp.Weapon,
        });

        var shieldCoordinates = _transform.GetMapCoordinates(shield);
        _transform.SetCoordinates(projectile, _transform.ToCoordinates(shieldCoordinates.Offset(normal * 0.15f)));
        _transform.SetLocalRotation(projectile, mapVelocity.ToAngle());
        _physics.SetLinearVelocity(projectile, Vector2.Zero, body: physics);
        _physics.SetCanCollide(projectile, false, body: physics);
        _physics.SetBodyType(projectile, BodyType.Static, body: physics);
        return true;
    }

    private void DetonateShield(Entity<XenoWarlockComponent> ent, EntityUid shield, bool fromBreak)
    {
        if (!TryComp(shield, out XenoWarlockShieldComponent? shieldComp))
            return;

        shieldComp.Manual = !fromBreak;
        shieldComp.Detonated = !fromBreak;
        Dirty(shield, shieldComp);
        ReleaseShieldProjectiles(shield, shieldComp, reflect: !fromBreak);
        ApplyShieldBlast(ent, shield, shieldComp);

        ent.Comp.ActiveShield = null;
        Dirty(ent);
        RemCompDeferred<RMCRootedComponent>(ent);

        if (_net.IsServer)
            QueueDel(shield);
    }

    private void OnShieldTerminating(Entity<XenoWarlockShieldComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp(ent.Comp.Warlock, out XenoWarlockComponent? warlock) && warlock.ActiveShield == ent.Owner)
        {
            warlock.ActiveShield = null;
            Dirty(ent.Comp.Warlock, warlock);
        }

        RemCompDeferred<RMCRootedComponent>(ent.Comp.Warlock);

        if (!ent.Comp.ProjectilesReleased)
            ReleaseShieldProjectiles(ent, ent.Comp, reflect: false);

        if (!TryComp(ent.Comp.Warlock, out XenoWarlockComponent? ownerWarlock))
            return;

        if (ent.Comp.Detonated || ent.Comp.Manual)
            return;

        if (ownerWarlock.CautiousMind)
        {
            ApplyShieldBlast((ent.Comp.Warlock, ownerWarlock), ent.Owner, ent.Comp);
            return;
        }

        _stun.TryParalyze(ent.Comp.Warlock, TimeSpan.FromSeconds(1), true);
    }

    private void ReleaseShieldProjectiles(EntityUid shield, XenoWarlockShieldComponent shieldComp, bool reflect)
    {
        if (shieldComp.ProjectilesReleased)
            return;

        shieldComp.ProjectilesReleased = true;
        var shieldCoordinates = _transform.GetMapCoordinates(shield);
        var normal = shieldComp.Direction.ToVec().Normalized();

        foreach (var frozen in shieldComp.FrozenProjectiles)
        {
            if (!EntityManager.EntityExists(frozen.Projectile) ||
                !TryComp(frozen.Projectile, out PhysicsComponent? physics) ||
                !TryComp(frozen.Projectile, out ProjectileComponent? projectile))
            {
                continue;
            }

            var releaseVelocity = reflect
                ? ReflectDirection(_physics.GetMapLinearVelocity(frozen.Projectile, component: physics), normal)
                : frozen.LinearVelocity;

            if (releaseVelocity.LengthSquared() <= 0.01f)
                releaseVelocity = normal * 8f;

            var releaseDirection = releaseVelocity.Normalized();
            _transform.SetCoordinates(frozen.Projectile, _transform.ToCoordinates(shieldCoordinates.Offset(releaseDirection * 0.75f)));
            _transform.SetLocalRotation(frozen.Projectile, releaseDirection.ToAngle());
            _physics.SetBodyType(frozen.Projectile, BodyType.Dynamic, body: physics);
            _physics.SetCanCollide(frozen.Projectile, true, body: physics);
            _physics.SetLinearVelocity(frozen.Projectile, releaseVelocity, body: physics);
            _physics.WakeBody(frozen.Projectile, body: physics);

            projectile.ProjectileSpent = false;
            projectile.Shooter = reflect ? shieldComp.Warlock : frozen.Shooter;
            projectile.Weapon = reflect ? shieldComp.Warlock : frozen.Weapon;
            Dirty(frozen.Projectile, projectile);
        }

        shieldComp.FrozenProjectiles.Clear();
    }

    private static Vector2 ReflectDirection(Vector2 incoming, Vector2 normal)
    {
        if (incoming.LengthSquared() <= 0.01f)
            return normal * 8f;

        var normalized = incoming.Normalized();
        return (normalized - 2 * Vector2.Dot(normalized, normal) * normal) * incoming.Length();
    }

    private void OnBuildMutationChamber(HiveBoonBuildMutationChamberEvent args)
    {
        if (_net.IsClient || args.Core is not { } core)
            return;

        var existing = GetMutationChamberCount(args.Performer, args.ChamberCategory);
        if (existing >= args.MaxCount)
        {
            _popup.PopupEntity(Loc.GetString("rmc-warlock-chamber-max"), args.Performer, args.Performer);
            return;
        }

        var offsets = new[]
        {
            new Vector2(1, 0),
            new Vector2(-1, 0),
            new Vector2(0, 1),
            new Vector2(0, -1),
            new Vector2(1, 1),
            new Vector2(-1, 1),
            new Vector2(1, -1),
            new Vector2(-1, -1),
        };

        foreach (var offset in offsets)
        {
            var coords = new EntityCoordinates(core, offset);
            var chamber = Spawn(args.ChamberPrototype, coords);
            _hive.SetSameHive(core, chamber);
            _popup.PopupEntity(Loc.GetString("rmc-warlock-chamber-built"), args.Performer, args.Performer);
            return;
        }
    }

    private bool CanUseMode(XenoWarlockComponent warlock, XenoWarlockBlastMode mode)
    {
        return mode switch
        {
            XenoWarlockBlastMode.PsychicDrain => warlock.DrainingBlast,
            XenoWarlockBlastMode.PsychicLance => warlock.Primordial,
            _ => true,
        };
    }

    private static FixedPoint2 GetModeCost(XenoWarlockBlastMode mode)
    {
        return mode switch
        {
            XenoWarlockBlastMode.PsychicDrain => 140,
            XenoWarlockBlastMode.PsychicLance => 180,
            _ => 120,
        };
    }

    private static float GetModeRange(XenoWarlockBlastMode mode)
    {
        return mode switch
        {
            XenoWarlockBlastMode.PsychicLance => 9f,
            _ => 7f,
        };
    }

    private string GetModeName(XenoWarlockBlastMode mode)
    {
        return mode switch
        {
            XenoWarlockBlastMode.PsychicDrain => Loc.GetString("rmc-warlock-mode-drain"),
            XenoWarlockBlastMode.PsychicLance => Loc.GetString("rmc-warlock-mode-lance"),
            _ => Loc.GetString("rmc-warlock-mode-blast"),
        };
    }

    private int GetMutationChamberCount(EntityUid xeno, XenoWarlockMutationCategory category)
    {
        var count = 0;
        var query = EntityQueryEnumerator<XenoMutationChamberComponent>();
        while (query.MoveNext(out var uid, out var chamber))
        {
            if (chamber.Category == category && _hive.FromSameHive(uid, xeno))
                count++;
        }

        return count;
    }

    private MapCoordinates? FindFirstTarget(Entity<XenoWarlockComponent> ent, MapCoordinates source, MapCoordinates target, float range, out EntityUid? hit)
    {
        hit = null;
        MapCoordinates? hitCoordinates = null;
        var bestDistance = float.MaxValue;

        var tiles = _line.DrawLine(_transform.ToCoordinates(source), _transform.ToCoordinates(target), TimeSpan.Zero, range, out _, ignoreBarricades: false);
        foreach (var tile in tiles)
        {
            _targets.Clear();
            _entityLookup.GetEntitiesInRange(tile.Coordinates.MapId, tile.Coordinates.Position, 0.45f, _targets, LookupFlags.Uncontained);
            foreach (var uid in _targets)
            {
                if (!_xeno.CanAbilityAttackTarget(ent, uid))
                    continue;

                var coords = _transform.GetMapCoordinates(uid);
                var distance = (coords.Position - source.Position).LengthSquared();
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                hit = uid;
                hitCoordinates = coords;
            }

            if (hit != null)
                return hitCoordinates;
        }

        return null;
    }

    private void ApplyShieldBlast(Entity<XenoWarlockComponent> ent, EntityUid shield, XenoWarlockShieldComponent shieldComp)
    {
        var coords = _transform.GetMapCoordinates(shield);
        var direction = shieldComp.Direction.ToVec().Normalized();
        _targets.Clear();
        _entityLookup.GetEntitiesInRange(coords.MapId, coords.Position, 2.1f, _targets, LookupFlags.Uncontained);
        foreach (var uid in _targets)
        {
            if (!_xeno.CanAbilityAttackTarget(ent, uid))
                continue;

            var targetCoords = _transform.GetMapCoordinates(uid);
            var offset = targetCoords.Position - coords.Position;
            if (Vector2.Dot(offset, direction) <= 0)
                continue;

            _damageable.TryChangeDamage(uid, ShieldDetonateDamage, armorPiercing: 10, origin: ent, tool: ent);
            _stun.TryParalyze(uid, TimeSpan.FromSeconds(1), true);
            _sizeStun.KnockBack(uid, coords, 2f, 2f, 10f, false);
        }
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var time = _timing.CurTime;
        var warlocks = EntityQueryEnumerator<XenoWarlockComponent>();
        while (warlocks.MoveNext(out var uid, out var warlock))
        {
            if (warlock.ActiveCrush != null && (_mobState.IsDead(uid) || _mobState.IsIncapacitated(uid)))
            {
                StopCrush((uid, warlock), false);
                continue;
            }

            if (warlock.ActiveCrush != null && time >= warlock.NextCrushExpandAt)
                ExpandCrush((uid, warlock));
        }

        var shields = EntityQueryEnumerator<XenoWarlockShieldComponent>();
        while (shields.MoveNext(out var uid, out var shield))
        {
            if (time >= shield.ExpiresAt)
            {
                shield.Manual = true;
                Dirty(uid, shield);
                QueueDel(uid);
            }
        }
    }
}
