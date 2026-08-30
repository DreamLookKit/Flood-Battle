/* - От 0.1 до 0.9 — «Тонущие объекты» (Тяжелее воды)
Физически это означает, что плотность объекта выше плотности воды. 
Даже если предмет полностью уйдет на дно, силы Архимеда не хватит, чтобы его поднять. 
Он будет реалистично лежать на дне, но падать сквозь воду чуть медленнее, чем в воздухе.
- Ровно 1.0 — «Идеальный баланс» (Нейтральная плавучесть)
Объект весит ровно столько же, сколько вытесненная им вода (как подводная лодка или рыба). 
На какой глубине вы его оставите, там он и зависнет.
- От 1.1 до 1.9 — «Тяжелая плавучесть» (Металл, сырое дерево)
Объекты будут плавать, но погружаясь в воду очень глубоко. 
Например, при 1.2 бочка будет торчать из воды всего на 15–20%, а остальная её часть будет скрыта под поверхностью.
- Ровно 2.0 — «Стандарт» (Сухое дерево, пластик)
Универсальная точка равновесия. Объект погружается ровно наполовину (на 50%). 
Выглядит отлично для большинства стандартных игровых коробок и бочек.
-От 2.1 до 5.0 — «Экстремальная плавучесть» (Пенопласт, мячи, воздух)
Объекты плавают строго на поверхности, едва касаясь воды дном (погружение на 10–20%). 
Если такую бочку насильно притопить скриптом или сбросить с высоты, она очень резво выскочит обратно наверх.
 */
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BuoyantObject : MonoBehaviour{
    // Переменные
    [Header("Buoyant settings")]
    //Коэффициент плавучести. 1.0 — баланс, 2.0 — плавает как пенопласт, меньше 1.0 — тонет
    [Tooltip("Buoyancy coefficient")]
    [Range(0.1f, 5.0f)]
    [SerializeField] private float floatingPower = 2.0f; // Возвращаем её в контекст кода!
    [Tooltip("Water movement drag (for smoothy braking object & not jumping like a bol)")]
    [SerializeField] private float waterDrag = 4f;
    [Header("Detection Settings")]
    [SerializeField] private LayerMask waterLayerMask; // Просто выбираешь слой Water в инспекторе
    [SerializeField] private float detectionRadius = 0.2f;
    [Header("Advanced Physics")]
    [Tooltip("Buoyancy points at the corners")]
    [SerializeField] private Vector3[] buoyancyPoints = new Vector3[]{
        new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f),
        new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, -0.5f),
        new Vector3(0f, 0f, 0f)
    };
    [Header("Detection Settings")]
    // Выделяем буфер под найденные коллайдеры ОДИН раз при запуске игры. 
    // Размер 4 — за глаза для одной точки. Мусорщик (GC) скажет тебе спасибо.
    private readonly Collider[] hitColliders = new Collider[4];
    private Rigidbody rb;
    private PlayerController pc; // Теперь эта ссылка видна ВСЕМ методам внутри этого файла!
    private bool isInsideWater = false;
    private bool isChestInWater = false;
    // Публичное свойство для проверки нахождения в воде
    public bool IsInWater => isInsideWater;     // Для всех объектов в воде
    public bool ChestInWater => isChestInWater; // Для игроков, включение анимации плавания при поднятии воды до груди
    private float objectHeight;
    private Collider myCollider;
    private RigidbodyConstraints originalConstraints;
    private Animator anim;
    // Start
    private void Start(){
        rb = GetComponent<Rigidbody>();
        pc = GetComponent<PlayerController>();
        // Ограничиваем максимальную глубину высотой самого объекта, 
        // чтобы сила выталкивания не росла бесконечно, когда объект полностью под водой.
        myCollider = GetComponent<Collider>();
        // Ищем аниматор на дочерней 3D-модели
        anim = GetComponentInChildren<Animator>();
        // Запоминаем, какие галочки стояли у объекта изначально
        originalConstraints = rb.constraints; 
        if(myCollider != null)
            objectHeight = myCollider.bounds.size.y;
        // Сдвигаем центр масс физического тела вниз на половину его высоты
        // Теперь физический «тяжелый низ» будет удерживать объект от переворотов
        rb.centerOfMass = new Vector3(0f, -objectHeight * 0.5f, 0f);
    }
    // Физическая сила всегда применяется в FixedUpdate (50 раз всекунду)
    private void FixedUpdate(){
        // Ищем только воду, используя маску!
        int numColliders = Physics.OverlapSphereNonAlloc(
            GetObjectBottom(), detectionRadius, hitColliders,
            waterLayerMask, QueryTriggerInteraction.Collide);
        int numColliders2 = Physics.OverlapSphereNonAlloc(
            GetChestPoint(), detectionRadius, hitColliders,
            waterLayerMask, QueryTriggerInteraction.Collide);
        float waterSurfaceY = float.MinValue;
        // Цикл по твоему массиву хитов
        if (numColliders > 0){ // Если хоть что-то нашли в воде
            isInsideWater = true;
            waterSurfaceY = hitColliders[0].bounds.max.y; // Фикс определения уровня воды
            // Считаем базовую силу на одну точку. Делим общую силу Архимеда на количество точек.
            float shareForcePerPoint = (floatingPower * rb.mass * Mathf.Abs(Physics.gravity.y)) / buoyancyPoints.Length;
            foreach (Vector3 localPoint in buoyancyPoints){
                // Просто переводим локальную точку в мировые координаты. Unity сам применит Scale и Rotation!
                Vector3 worldPointPos = transform.TransformPoint(localPoint);
                float pointDepth = waterSurfaceY - worldPointPos.y;
                if (pointDepth > 0){
                    // ХАК 1: Нелинейное погружение (корень делает поведение в воде мягче)
                    float linearRatio = Mathf.Clamp01(pointDepth / objectHeight);
                    float immersionRatio = Mathf.Sqrt(linearRatio); // Мягкий старт, упругий финал
                    // Базовая выталкивающая сила для точки
                    Vector3 buoyantForce = Vector3.up * shareForcePerPoint * immersionRatio;
                    // ХАК 2: Локальное водяное сопротивление (Демпфирование точки)
                    // Находим скорость конкретно ЭТОЙ точки в мировом пространстве
                    Vector3 pointVelocity = rb.GetPointVelocity(worldPointPos);   
                    // Сила сопротивления направлена против движения точки и зависит от её скорости и Drag
                    Vector3 dampingForce = -pointVelocity * waterDrag * immersionRatio;
                    // Итоговая сила = Выталкивание + Гашение колебаний
                    Vector3 totalPointForce = buoyantForce + dampingForce;
                    // Применяем в точку
                    rb.AddForceAtPosition(totalPointForce, worldPointPos, ForceMode.Force);
                }
            }
        }else 
            isInsideWater = false;
        if(numColliders2 > 0){  //Если грудная клетка в воде
            isChestInWater = true;
        }else
            isChestInWater = false;
    }
    /* private void OnDrawGizmosSelected(){
        Gizmos.color = isInsideWater == true ? Color.blue : Color.white;
        Gizmos.DrawWireSphere(GetObjectBottom(), detectionRadius);
        if(pc != null && anim != null){
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(GetChestPoint(), detectionRadius);
        }
    } */
    private Vector3 GetObjectBottom(){
        if (myCollider != null)
            return new Vector3(transform.position.x, myCollider.bounds.min.y + 0.2f, transform.position.z);
        return transform.position;
    }
    private Vector3 GetChestPoint(){
        if (pc != null && anim != null){
            Transform chestBone = anim.GetBoneTransform(HumanBodyBones.Chest);
            if (chestBone != null)
                return chestBone.position;  //new Vector3() писать не нужно, position уже Vector3
        }
        return transform.position;
    }
}