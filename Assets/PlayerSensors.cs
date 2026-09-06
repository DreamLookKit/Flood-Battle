using UnityEngine;
[RequireComponent(typeof(PlayerController))]
public class PlayerSensors : MonoBehaviour 
{
    [Header("Detection Settings")]
    [SerializeField] private LayerMask groundLayer;     // Маска слоя Ground
    [SerializeField] private LayerMask raftLayer;     // Маска слоя Ground
    [SerializeField] private LayerMask objectLayer;     // Маска слоя Ground
    [SerializeField] private LayerMask waterLayer;      // Маска слоя Water
    [SerializeField] private LayerMask detectionMask;   // Маска слоев (Земля, Плот, Объекто, Вода)
    [SerializeField] private float detectionRadius = 0.2f; // Радиус сферы детекции под ногами
    [SerializeField] private float landingDistance = 1.7f; // Дистанция до земли для срабатывания анимации приземления
    public enum PlayerState { Nothing, Raft, Object, Ground, Water} 
    public PlayerState CurrentPlayerChest { get; private set; } = PlayerState.Nothing;
    public PlayerState LastPlayerChest { get; private set; } = PlayerState.Nothing;
    public PlayerState CurrentPlayerLegs { get; private set; } = PlayerState.Nothing;
    public PlayerState LastPlayerLegs { get; private set; } = PlayerState.Nothing;
    public PlayerState CurrentPlayerBelowLegs { get; private set; } = PlayerState.Nothing;
    public PlayerState LastPlayerBelowLegs { get; private set; } = PlayerState.Nothing;
    private Collider myCollider;
    private Rigidbody rb;
    private Animator anim;
    private Transform chestBone;
    private readonly Collider[] hitCollidersChest = new Collider[4]; 
    private readonly Collider[] hitCollidersLegs = new Collider[4];
    private readonly RaycastHit[] hitCollidersLanding = new RaycastHit[4];   
    // ОБЪЯВЛЕНИЕ: Создаем ячейки для хранения числовых ID.
    // readonly означает, что мы запишем туда число один раз и никто его случайно не изменит.
    // static экономит память — эти ID будут общими для всех копий скрипта.
    private static readonly int SensWaterHash = Animator.StringToHash("senWater");
    private static readonly int SensGroundHash = Animator.StringToHash("senGround");
    private static readonly int SensBuoyant = Animator.StringToHash("senBuoyant");
    private static readonly int SensLandingHash = Animator.StringToHash("senLanding"); 
    private void Awake(){
    }
    private void Start(){
        rb = GetComponent<Rigidbody>();
        myCollider = GetComponent<Collider>();
        anim = GetComponentInChildren<Animator>();
        if (anim != null)
            chestBone = anim.GetBoneTransform(HumanBodyBones.Chest);
    }
    private void FixedUpdate(){
        // 1. Проверяем, вода у игрока на уровне гурди или нет - GetPlayerChest()
        int overlapSphereChest = Physics.OverlapSphereNonAlloc(
            GetPlayerChest(), detectionRadius, hitCollidersChest,
            waterLayer, QueryTriggerInteraction.Collide);
        if(overlapSphereChest > 0)
            CurrentPlayerChest = PlayerState.Water;
        else
            CurrentPlayerChest = PlayerState.Nothing;
        // Если на уровне груди изменения по сравнению с предыдущим сохраненным флагом
        if (LastPlayerChest != CurrentPlayerChest){
            Debug.Log($"Change layer near CHEST: {LastPlayerChest} -> {CurrentPlayerChest}");
            anim.SetBool(SensBuoyant, CurrentPlayerChest == PlayerState.Water);
            LastPlayerChest = CurrentPlayerChest;
        }
        // 2. Проверяем, вода или земля на уровне ног - GetPlayerLegs()
        int overlapSphereLegs = Physics.OverlapSphereNonAlloc(
            GetPlayerLegs(0.1f), detectionRadius, hitCollidersLegs,
            detectionMask, QueryTriggerInteraction.Collide);
        if(overlapSphereLegs > 0){
            int accumulatedMask = 0;
            for (int i = 0; i < overlapSphereLegs; i++) {
                // Берем номер слоя (от 0 до 4)
                int layer = hitCollidersLegs[i].gameObject.layer;
                // Превращаем номер в бит (например, слой 3 станет 1 << 3 = 00001000) и закидываем в общую корзину через побитовое ИЛИ (|=)
                accumulatedMask |= (1 << layer);
            }
            CurrentPlayerLegs = ((accumulatedMask & waterLayer.value) != 0) ? PlayerState.Water :
                                 ((accumulatedMask & groundLayer.value) != 0) ? PlayerState.Ground :
                                 PlayerState.Nothing;
        }else CurrentPlayerLegs = PlayerState.Nothing;
        // Если на уровне ног изменения по сравнению с предыдущим сохраненным флагом
        if(LastPlayerLegs != CurrentPlayerLegs){
            Debug.Log($"Change layer near LEGS: {LastPlayerLegs} -> {CurrentPlayerLegs}");
            // Выключаем то, из чего вышли
            switch (LastPlayerLegs){
                case PlayerState.Water:  anim.SetBool(SensWaterHash, false); break;
                case PlayerState.Ground: anim.SetBool(SensGroundHash, false); break;
            }
            // Включаем то, куда пришли
            switch (CurrentPlayerLegs){
                case PlayerState.Water:  anim.SetBool(SensWaterHash, true); break;
                case PlayerState.Ground: anim.SetBool(SensGroundHash, true); break;
            }
            LastPlayerLegs = CurrentPlayerLegs;
        }
        // 3. Находим землю под ногами игрока для анимации приземления
        int sphereCastLanding = Physics.SphereCastNonAlloc(
            GetPlayerLegs(), detectionRadius, Vector3.down, hitCollidersLanding, landingDistance, detectionMask, QueryTriggerInteraction.Ignore);
        if (sphereCastLanding > 0 && rb.linearVelocity.y < -3f)
            CurrentPlayerBelowLegs = PlayerState.Ground;
        else CurrentPlayerBelowLegs = PlayerState.Nothing;
        // Если под ногами изменения по сравнению с предыдущим сохраненным флагом
        if (LastPlayerBelowLegs != CurrentPlayerBelowLegs){
            Debug.Log($"Change layer BELOW LEGS: {LastPlayerBelowLegs} -> {CurrentPlayerBelowLegs}");
            anim.SetBool(SensLandingHash, CurrentPlayerBelowLegs == PlayerState.Ground);
            LastPlayerBelowLegs = CurrentPlayerBelowLegs;
        }
    }
    private void OnDrawGizmosSelected(){
        // Отрисовываем тестовую сферу в груди игрока
        Gizmos.color = CurrentPlayerChest switch{
            PlayerState.Water   => Color.white,
            _                   => Color.white
        };
        Gizmos.DrawWireSphere(GetPlayerChest(), detectionRadius);
        // Отрисовываем тестовую сферу в ногах игрока
        Gizmos.color = CurrentPlayerLegs switch{
            PlayerState.Ground  => Color.green,
            PlayerState.Water   => Color.blue,
            _                   => Color.white
        };
        Gizmos.DrawWireSphere(GetPlayerLegs(0.1f), detectionRadius);
    }
    private Vector3 GetPlayerChest(){
        if (chestBone != null)
            return chestBone.position;  //new Vector3() писать не нужно, position уже Vector3
        return transform.position + Vector3.up * 1.5f; // Фолбэк, если кость не нашлась
    }    
    private Vector3 GetPlayerLegs(float correctY = 0f){
        if (myCollider != null)
            return new Vector3(transform.position.x, myCollider.bounds.min.y + correctY, transform.position.z);
        return transform.position;
    }
}
