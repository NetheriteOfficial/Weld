using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace Weld.Physics.Bepu;

public struct WeldNarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public PhysicsMaterials Materials;
    public SpringSettings ContactSpringiness;
    public float MaximumRecoveryVelocity;

    public WeldNarrowPhaseCallbacks(PhysicsMaterials materials)
    {
        Materials = materials;
        ContactSpringiness = new SpringSettings(30, 1);
        MaximumRecoveryVelocity = 2f;
    }

    public void Initialize(Simulation simulation) { }

    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        => a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;

    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial.FrictionCoefficient = Materials.FrictionFor(pair.A) * Materials.FrictionFor(pair.B);
        pairMaterial.MaximumRecoveryVelocity = MaximumRecoveryVelocity;
        pairMaterial.SpringSettings = ContactSpringiness;
        return true;
    }

    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => true;

    public void Dispose() { }
}

public struct WeldPoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public Vector3 Gravity;
    public float LinearDamping;
    public float AngularDamping;
    private Vector3Wide _gravityDt;
    private Vector<float> _linearDampingDt;
    private Vector<float> _angularDampingDt;

    public WeldPoseIntegratorCallbacks(Vector3 gravity, float linearDamping = 0.03f, float angularDamping = 0.03f)
    {
        Gravity = gravity;
        LinearDamping = linearDamping;
        AngularDamping = angularDamping;
        _gravityDt = default;
        _linearDampingDt = default;
        _angularDampingDt = default;
    }

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;

    public void Initialize(Simulation simulation) { }

    public void PrepareForIntegration(float dt)
    {
        _linearDampingDt = new Vector<float>(MathF.Pow(Math.Clamp(1 - LinearDamping, 0, 1), dt));
        _angularDampingDt = new Vector<float>(MathF.Pow(Math.Clamp(1 - AngularDamping, 0, 1), dt));
        _gravityDt = Vector3Wide.Broadcast(Gravity * dt);
    }

    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
    {
        velocity.Linear = (velocity.Linear + _gravityDt) * _linearDampingDt;
        velocity.Angular *= _angularDampingDt;
    }
}
