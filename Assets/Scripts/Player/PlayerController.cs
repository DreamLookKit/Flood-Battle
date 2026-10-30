using FishNet;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.InputSystem;
// TEST
using TMPro;
//
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]


public class PlayerController : NetworkBehaviour
{
    [Header("Breath Settings")]
    [SerializeField] private float breathSpeed = 5f;          // Скорость покачивания при дыхании
    [SerializeField] private float breathAmplitude = 0.02f;   // Амплитуда (насколько сильно качается взгляд)
    [Header("Movement Speed Settings")]
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float backSpeed = 1.7f;
    [SerializeField] private float sprintSpeed = 5f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float waterVerticalSpeed = 5f;
    [SerializeField] private float mouseSensivity = 2f;
    [Header("Movement Physics Settings")]
    [SerializeField] private float acceleration = 16f;
    [SerializeField] private float deceleration = 14f;
    [SerializeField][Range(0f, 1f)] private float airControlFactor = 0.15f;    //Для контроля прыжка
    [SerializeField] private float jumpForce = 5f;
    [Header("Camera Settings")]
    [SerializeField] private float standHeight = 2f;        // Стандартная высота игрока
    [SerializeField] private float crouchHeight = 1.0f;     // Высота игрока в приседе
    [SerializeField] private float crouchSmoothTime = 8f;   // Скорость плавного опускания камеры
    [SerializeField][Range(0.5f, 1.0f)] private float cameraHeightRatio = 0.85f;   // Eye level (percentage of body height)
    [Header("Animation Settings")]
    [SerializeField] private Animator _anim;
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    // TEST
    private TMP_Text _debugSpeedText;
    //
    // Все нажатия детектятся в Update, а действия происходят в FixedUpdate
    private bool _jumpWasPressed;  // Флаг-мостик между Update и FixedUpdate
    // Мы делаем ссылки на действия публичными, чтобы скрипт меню настроек мог получить к ним доступ
    public InputAction MoveAction { get; private set; }
    public InputAction LookAction { get; private set; }
    public InputAction SprintAction { get; private set; }
    public InputAction JumpAction { get; private set; }
    public InputAction CrouchAction { get; private set; }
    private Rigidbody _rb;
    private CapsuleCollider _capsuleCollider;    // Ссылка для изменения высоты тела
    private PlayerSensor _playerSensor;     //  Локальная переменная-ссылка на сторонний скрпит PlayerSensor.cs
    // ОБЪЯВЛЕНИЕ: Создаем ячейки для хранения числовых ID.
    // readonly означает, что мы запишем туда число один раз и никто его случайно не изменит.
    // static экономит память — эти ID будут общими для всех копий скрипта.
    private static readonly int _SpeedHash = Animator.StringToHash("Speed");
    private static readonly int _IsCrouchedHash = Animator.StringToHash("IsCrouched");
    private enum JumpState { Grounded, InNormalJump, InLongJump, LandingProcessed }
    private JumpState _currentJumpState = JumpState.Grounded;
    private float _speedForAnimator = 0f;
    private float _cameraRotationX = 0f;
    private float _visualRotationY = 0f;
    private float _accumulateMouseX = 0f;
    private float _defaultY = 0f;    // Стандартное положение камеры по оси Y
    // Структура, в который клиент упаковывает все нажатия
    public struct MoveData : IReplicateData
    {
        public Vector2 MoveInput;
        public bool JumpPressed, SprintPressed, CrouchPressed;
        public float RotationY;
        public PlayerSensor.PlayerState ChestState, LegsState, BelowLegsState;
        // Конструктор для удобства заполнения
        public MoveData(Vector2 moveInput, bool jump, bool sprint, bool crouch, float rotationY, PlayerSensor.PlayerState chest, PlayerSensor.PlayerState legs, PlayerSensor.PlayerState belowLegs)
        {
            MoveInput = moveInput;
            JumpPressed = jump;
            SprintPressed = sprint;
            CrouchPressed = crouch;
            RotationY = rotationY;
            ChestState = chest;
            LegsState = legs;
            BelowLegsState = belowLegs;
            _tick = 0; // Обязательное поле для FishNet
        }
        // Внутренности FishNet для отслеживания сетевых тиков
        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }
    // Структура, при помощи которой сервер серяет данные из ReplicateMove
    public struct ReconcileData : IReconcileData
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 LinearVelocity;
        public ReconcileData(Vector3 position, Quaternion rotation, Vector3 linearVelocity)
        {
            Position = position;
            Rotation = rotation;
            LinearVelocity = linearVelocity;
            _tick = 0;
        }
        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }
    private void Awake()
    {
        _anim = GetComponentInChildren<Animator>();
        _rb = GetComponent<Rigidbody>();
        _capsuleCollider = GetComponent<CapsuleCollider>();      // Поиск нашего коллайдера
        // Создаем действие для обзора мышью
        LookAction = new InputAction("Look", binding: "<Mouse>/delta");
        // Мы создаем составной 2D-вектор для WASD
        // Каждой кнопке жестко присвоен индекс (ID) от 1 до 4; это критически важно для будущего переназначения клавиш!
        MoveAction = new InputAction("Move");
        MoveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")     // Индекс привязки: 1
            .With("Down", "<Keyboard>/s")   // Индекс привязки: 2
            .With("Left", "<Keyboard>/a")   // Индекс привязки: 3
            .With("Right", "<Keyboard>/d"); // Индекс привязки: 4
        JumpAction = new InputAction("Jump", binding: "<Keyboard>/space");
        SprintAction = new InputAction("Sprint", binding: "<Keyboard>/leftShift");
        CrouchAction = new InputAction("Crouch", binding: "<Keyboard>/leftCtrl");
        _playerSensor = GetComponent<PlayerSensor>(); // Кэшируем переменную-ссылку на PlayerSensor.cs
        // Автоматически находим камеру среди дочерних объектов, если она не была перетащена в Инспектор
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
            _defaultY = playerCamera.transform.localPosition.y;            // Записываем ссылку на камеру
        }
        // Блокируем курсор мыши в центре экрана, чтобы он не покидал окно игры
        Cursor.lockState = CursorLockMode.Locked;
        // Скрываем его
        Cursor.visible = false;
    }
    private void Start()
    {
        
    }
    public override void OnStartClient()
    {
        base.OnStartClient();
        // Подписываем ВСЕХ (и сервер, и клиентов) на сетевые тики FishNet
        if (InstanceFinder.TimeManager != null)
            InstanceFinder.TimeManager.OnTick += TimeManager_OnTick;
        if (!IsOwner) return;
        // Включаем рендеринг камеры для себя
        if (playerCamera != null) playerCamera.enabled = true;
        // Включаем уши для себя 
        AudioListener myListener = GetComponentInChildren<AudioListener>(true);
        if (myListener != null) myListener.enabled = true;
        // Включаем импуты только если мы хозяева этого персонажа
        if (LookAction != null) LookAction.Enable();
        if (MoveAction != null) MoveAction.Enable();
        if (JumpAction != null) JumpAction.Enable();
        if (SprintAction != null) SprintAction.Enable();
        if (CrouchAction != null) CrouchAction.Enable();
        if (JumpAction != null) JumpAction.performed += OnJumpPerformed;
        // TEST
        GameObject textObj = GameObject.Find("Text_Test");
        if (textObj != null)
            _debugSpeedText = textObj.GetComponent<TMP_Text>();
        else
            Debug.LogWarning("[Speed Debug] Не найден объект 'DebugSpeedText' на сцене! Индикатор работать не будет.");
        //
    }
    public override void OnStopClient()
    {
        base.OnStopClient();
        // Обязательно отписываемся от тиков, чтобы не было утечек памяти!
        if (InstanceFinder.TimeManager != null)
            InstanceFinder.TimeManager.OnTick -= TimeManager_OnTick;
        // Выключаем и отписываемся при отключении
        if (!IsOwner) return;
        LookAction.Disable();
        MoveAction.Disable();
        JumpAction.Disable();
        SprintAction.Disable();
        CrouchAction.Disable();
        JumpAction.performed -= OnJumpPerformed;
    }
    // Графика и Логика (Привязана к FPS: 60, 100, 144 - неважно)
    private void Update()
    {
        if (!IsOwner) return; //Только игрок может управлять персонажем
        // Детекция мыши
        if (Mouse.current != null)
        {
            // Мы получаем изменение положения мыши для текущего кадра (30f - компенсация медленного движения мыши)
            Vector2 mouseDelta = LookAction.ReadValue<Vector2>() * (mouseSensivity * 30f) * Time.deltaTime;
            // Считаем углы автономно от физики transform-a!
            _cameraRotationX = Mathf.Clamp(_cameraRotationX - mouseDelta.y, -85, 85f);
            // Копим чистый сдвиг мыши для сети(дельту)
            _accumulateMouseX += mouseDelta.x;
            // Крутим камеру на 144 FPS для плавности обзора
            playerCamera.transform.localRotation = Quaternion.Euler(_cameraRotationX, 0f, 0f);
        }
        // 2. СГЛАЖИВАНИЕ МОДЕЛЬКИ (Чтобы ноги не дергались из-за тиков сети)
        if (_anim != null)
        {
            // Моделька плавно докручивается к углу взгляда, но ТОЛЬКО ПО ОСИ Y! Никаких Х!
            float smoothedY = Mathf.LerpAngle(_anim.transform.localRotation.eulerAngles.y, 0f, 15f * Time.deltaTime);
            _anim.transform.localRotation = Quaternion.Euler(0f, smoothedY, 0f);
        }
        // ПЕРЕДАЧА ПАРАМЕТРОВ В АНИМАТОР
        if (playerCamera != null && _playerSensor != null)
            if (_playerSensor.CurrentPlayerLegs.Value != PlayerSensor.PlayerState.Water)
                HandleCrouch();
    }
    // Синхронизация тиков по сети между клиентами
    private void TimeManager_OnTick()
    {
        if (!IsOwner || _playerSensor == null || _rb == null) return;
        float targetNetworkRotationY = transform.localRotation.eulerAngles.y + _accumulateMouseX;
        // Клиент-владелец собирает данные со своей клавиатуры
        MoveData md = new MoveData(
            MoveAction.ReadValue<Vector2>(),
            _jumpWasPressed,
            SprintAction.IsPressed(),
            CrouchAction.IsPressed(),
            targetNetworkRotationY,
            _playerSensor.CurrentPlayerChest.Value,
            _playerSensor.CurrentPlayerLegs.Value,
            _playerSensor.CurrentPlayerBelowLegs.Value
        );
        // Сбрасываем флаг прыжка, так как мы его уже упаковали в этот тик
        _jumpWasPressed = false;
        // Очищает мешок! Сеть забрала данные, копить их больше нельхя
        _accumulateMouseX = 0f;
        // Вызываем репликацию (клиент выполнит локально, а пакет улетит на сервер)
        ReplicateMove(md);
    }
    [Replicate]
    // Выполнение на локальном клиенте
    private void ReplicateMove(MoveData md, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        // Мгновенно меняем высоту физического коллайдера на сервере и клиенте под текущий тик
        SetColliderHeightDirect(md.CrouchPressed);
        // Нам плевать, что прислал клиент в пакете md. Мы верим только физике движка прямо здесь и сейчас!
        PlayerSensor.PlayerState currentLegsState = _playerSensor.CheckLegsState();
        // Разворачиваем коллайдер
        transform.rotation = Quaternion.Euler(0f, md.RotationY, 0f);
        // Считаем текущую горизонтальную скорость тела игрока
        Vector3 horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        float speed = horizontalVelocity.magnitude;
        // Сетевая обработка прыжка на основе данных из md
        if (md.JumpPressed && currentLegsState != PlayerSensor.PlayerState.Water)
            _rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
        // Вычисляем физическое направление движения
        Vector3 moveDir = (transform.forward * md.MoveInput.y + transform.right * md.MoveInput.x).normalized;
        // Определяем, что мы сейчас делаем: разгоняемся (WASD зажат) или тормозим (WASD отпущен)
        float currentRate = (md.MoveInput.magnitude > 0) ? acceleration : deceleration;
        // Если мы НЕ на земле и НЕ в воде — срезаем силу торможения/разгона (для более длительного прыжка)
        if (currentLegsState == PlayerSensor.PlayerState.Nothing) currentRate *= airControlFactor;
        // Устанавливаем стандартную скорость ходьбыаем
        float currentSpeed = walkSpeed;
        // Сохраняем текущую вертикальную скорость
        float targetVelocityY = _rb.linearVelocity.y;
        // Если мы в воде
        if (currentLegsState == PlayerSensor.PlayerState.Water)
        {
            Debug.Log("In water");
            switch (md.CrouchPressed, md.SprintPressed)
            {
                case (true, _):
                    // Нажата кнопка приседа — активно погружаемся на глубину
                    //Умножение на 0.4f - снизили скорость погружения
                    targetVelocityY = -waterVerticalSpeed * 0.3f;
                    currentSpeed = walkSpeed * 0.5f;
                    currentRate /= 2.5f;
                    break;
                case (false, true):
                    currentSpeed = sprintSpeed * 0.5f;  // Спринт в воде - сравнима с со скоростью хотьбы на суше
                    currentRate /= 1.2f;
                    break;
                default:
                    currentSpeed = walkSpeed * 0.5f;    // Обычная скорость в воде 
                    currentRate /= 2.5f;
                    break;
            }
        }
        // Если мы на суше
        else
        {
            currentSpeed = (md.CrouchPressed, md.SprintPressed, md.MoveInput.y >= 0) switch
            {
                (true, _, _) => crouchSpeed, // Нажат присяд, все равно на спринт и направление движения
                (false, _, false) => backSpeed, // Не нажат присяд, не нажат спринт, движение назад
                (false, true, true) => sprintSpeed, // Не нажат присяд, нажат спринт, движение вперед     
                _ => walkSpeed    // В остальных случаях
            };
        }
        // Окончательное урезание скорости в воде и на суше, если движение задом
        //if (md.MoveInput.y < -0.1f) currentSpeed = 1.0f;
        // Идеальная горизонтальная скорость, которую хочет получить игрок прямо сейчас
        Vector3 normalVelocity = moveDir * currentSpeed;
        // ПЛАВНО подтягиваем ТЕКУЩУЮ скорость Rigidbody для плавности старта после нажатия клавиш
        float targetVelocityX = Mathf.MoveTowards(_rb.linearVelocity.x, normalVelocity.x, currentRate * Time.fixedDeltaTime);
        float targetVelocityZ = Mathf.MoveTowards(_rb.linearVelocity.z, normalVelocity.z, currentRate * Time.fixedDeltaTime);
        // Применяем скорость нативно
        if (currentLegsState == PlayerSensor.PlayerState.Water && !md.CrouchPressed)
            // Если мы в воде и НЕ жмем Ctrl (погружение) — мы позволяем физике Архимеда свободно двигать игрока вверх/вниз!
            // Мы перезаписываем ТОЛЬКО горизонтальную скорость X и Z, не трогая Y!
            _rb.linearVelocity = new Vector3(targetVelocityX, _rb.linearVelocity.y, targetVelocityZ);
        else
            // Если мы на суше ИЛИ насильно топим себя через Ctrl — применяем targetVelocityY как обычно
            _rb.linearVelocity = new Vector3(targetVelocityX, targetVelocityY, targetVelocityZ);
        // Рассчитываем виртуальную целевую скорость, которую хочет игрок
        float targetSpeed = 0f;
        if (md.MoveInput.magnitude > 0.05f)
        {
            targetSpeed = (md.CrouchPressed, md.SprintPressed, md.MoveInput.y >= 0f) switch
            {
                // 1. ПРИСЯД (Самый важный). Проверяем направление движения внутри
                (true, _, true) => crouchSpeed,   // Присел, идет вперед
                (true, _, false) => -crouchSpeed,  // Присел, идет назад (анимация приседа назад)
                // 2. ДВИЖЕНИЕ НАЗАД БЕЗ ПРИСЕДА (Спринт назад обычно невозможен, так что на него пофиг)
                (false, _, false) => -backSpeed,   // Не присел, идет назад
                // 3. ДВИЖЕНИЕ ВПЕРЕД БЕЗ ПРИСЕДА
                (false, true, true) => sprintSpeed, // Не присел, зажал спринт, идет вперед — бег
                (false, false, true) => walkSpeed   // Не присел, без спринта, идет вперед — шаг
            };
        }  
        if(_anim != null)
        {
            // Намертво центрируем модель внутри капсулы, блокируя любые инерционные сдвиги
            _anim.transform.localPosition = new Vector3(0f, -0.01f, 0f);
            // Плавно подтягиваем текущее значание анимации к целевому
            _speedForAnimator = Mathf.MoveTowards(_speedForAnimator, targetSpeed, acceleration * Time.fixedDeltaTime);
            // Передаем плавное значение в Аниматор
            // Дополнительно используем встроенный демпер Unity (Damp Time = 0.05f), чтобы сгладить микро-шаги
            _anim.SetFloat(_SpeedHash, _speedForAnimator, 0.05f, Time.fixedDeltaTime);
            // Передаем положение стоит/присяд
            _anim.SetBool(_IsCrouchedHash, md.CrouchPressed);
            if (IsOwner && _debugSpeedText != null)
                _debugSpeedText.text = $"Real speed (Raw): {speed:F2}\nAnim speed (Smooth): {_speedForAnimator:F2}";
            // Детекция приседа
        }        
        // --- А ТЕПЕРЬ СЕРВЕР ОБНОВЛЯЕТ SYNCVAR ДЛЯ ДРУГИХ ИГРОКОВ ---
        // state == ReplicateState.Current означает, что мы считаем НАСТОЯЩИЙ тик, а не пересчитываем историю (Rollback)
        if (IsServerInitialized && state == ReplicateState.Ticked)
            _playerSensor.UpdateSyncVarsForAnimations();
    }
    [Reconcile]
    // Метод проверки игрока: позиция, угол поворота, скорость
    private void ReconcileMove(ReconcileData rd, Channel channel = Channel.Unreliable)
    {
        // Запоминаем старый поворот до отката
        float oldY = transform.rotation.eulerAngles.y;
        // Принудительная перезапись данных клиента данными от сервера (rd)
        transform.position = rd.Position;
        transform.rotation = rd.Rotation;
        _rb.linearVelocity = rd.LinearVelocity;
        // Считаем, на сколько градусов ошибся клиент
        float errorY = Mathf.DeltaAngle(rd.Rotation.eulerAngles.y, oldY);
        // Корректируем глаза на эту ошибку, а не швыряем их в дефолт сервера!
        _visualRotationY = transform.rotation.eulerAngles.y + errorY;
    }
    // Нажатие ctrl, камера визуально плавно опускается на клиенте
    private void HandleCrouch()
    {
        // Определяем целевую высоту камеры на основе ввода
        float targetCameraY = CrouchAction.IsPressed() ? (crouchHeight * cameraHeightRatio) : (standHeight * cameraHeightRatio);
        // Плавное сглаживание, независимое от FPS
        float lerpFactor = 1f - Mathf.Exp(-crouchSmoothTime * Time.deltaTime);
        _defaultY = Mathf.Lerp(_defaultY, targetCameraY, lerpFactor);
    }
    // Нажатие ctrl, коллайдер игрока жестко опускается на клиенте, а сервер подхватывает и опускает у себя тоже
    private void SetColliderHeightDirect(bool isCrouched)
    {
        // Выставляем жесткую высоту коллайдера в зависимости от флага из MoveData
        float targetHeight = isCrouched ? crouchHeight : standHeight;
        _capsuleCollider.height = targetHeight;
        // Центрируем капсулу, чтобы подошва всегда была на нуле
        _capsuleCollider.center = new Vector3(0f, targetHeight/2f, 0f);
    }
    // Детекция прыжка
    private void OnJumpPerformed(InputAction.CallbackContext context)
    {
        // Если ноги не в воде — взводим флаг прыжка для следующего сетевого тика
        if (_playerSensor != null && _playerSensor.CurrentPlayerLegs.Value != PlayerSensor.PlayerState.Water)
            _jumpWasPressed = true;
    }
    // Исполняется на СЕРВЕРЕ!
    public override void CreateReconcile()
    {
        base.CreateReconcile();
        // Сервер упаковывает свои эталонные данные
        ReconcileData rd = new ReconcileData(transform.position, transform.rotation, _rb.linearVelocity);
        // И сразу же вызываем наш метод сверки
        ReconcileMove(rd);
    }
}
