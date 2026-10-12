using FishNet;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.InputSystem;
using static PlayerSensor;
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]


public class PlayerController : NetworkBehaviour
{
    [Header("Breath Settings")]
    [SerializeField] private float breathSpeed = 5f;          // Скорость покачивания при дыхании
    [SerializeField] private float breathAmplitude = 0.02f;   // Амплитуда (насколько сильно качается взгляд)
    [Header("Movement Speed Settings")]
    [SerializeField] private float walkSpeed = 2f;
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
    [SerializeField] private Animator anim;
    [Header("References")]
    [SerializeField] private Camera playerCamera;
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
    private BuoyantObject _buoyantScript;
    private PlayerSensor _playerSensor;     //  Локальная переменная-ссылка на сторонний скрпит PlayerSensor.cs
    private enum JumpState { Grounded, InNormalJump, InLongJump, LandingProcessed }
    private JumpState _currentJumpState = JumpState.Grounded;
    // ОБЪЯВЛЕНИЕ: Создаем ячейки для хранения числовых ID.
    // readonly означает, что мы запишем туда число один раз и никто его случайно не изменит.
    // static экономит память — эти ID будут общими для всех копий скрипта.
    private static readonly int _SpeedHash = Animator.StringToHash("Speed");
    private static readonly int _WaterHash = Animator.StringToHash("Water");
    private static readonly int _GroundHash = Animator.StringToHash("Ground");
    private static readonly int _BuoyantHash = Animator.StringToHash("Buoyant");
    private static readonly int _IsCrouchedHash = Animator.StringToHash("IsCrouched");
    private static readonly int _LandingHash = Animator.StringToHash("Landing");
    private float _cameraRotationX = 0f;
    //private float currentCameraY;   // Текущая локальная высота камеры
    private float _speed;            // Текущая скорость игрока
    private float _speedForAnim;     // Текущая скорость игрока, которая будет передваться в аниматор с измененным знаком
    private float _defaultY = 0f;    // Стандартное положение камеры по оси Y
    private float _timer = 0f;
    //📦 Структура, в который клиент упаковывает все нажатия
    public struct MoveData : IReplicateData
    {
        public Vector2 MoveInput;
        public bool JumpPressed, SprintPressed, CrouchPressed;
        // Конструктор для удобства заполнения
        public MoveData(Vector2 moveInput, bool jump, bool sprint, bool crouch)
        {
            MoveInput = moveInput;
            JumpPressed = jump;
            SprintPressed = sprint;
            CrouchPressed = crouch;
            _tick = 0; // Обязательное поле для FishNet
        }
        // Внутренности FishNet для отслеживания сетевых тиков
        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }
    //📦 Структура, при помощи которой сервер серяет данные из ReplicateMove
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
    private void Start()
    {
        anim = GetComponentInChildren<Animator>();
        _rb = GetComponent<Rigidbody>();
        _capsuleCollider = GetComponent<CapsuleCollider>();      // Поиск нашего коллайдера
        _buoyantScript = GetComponent<BuoyantObject>();          // Поиск плавучести
        _playerSensor = GetComponent<PlayerSensor>();            // Кэшируем переменную-ссылку на PlayerSensor.cs
        // Ищем аниматор на дочерней 3D-модели
        // Автоматически находим камеру среди дочерних объектов, если она не была перетащена в Инспектор
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
            _defaultY = playerCamera.transform.localPosition.y;            // Записываем ссылку на камеру
        }
        // Блокируем курсор мыши в центре экрана, чтобы он не покидал окно игры
        Cursor.lockState = CursorLockMode.Locked;
        // Скрываем его
        Cursor.visible = false;
    }
    private void Awake()
    {
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
        SprintAction = new InputAction("Sprint", binding: "<Keyboard>/leftShift");
        CrouchAction = new InputAction("Crouch", binding: "<Keyboard>/leftCtrl");
    }
    public override void OnStartClient()
    {
        base.OnStartClient();
        // Подписываем ВСЕХ (и сервер, и клиентов) на сетевые тики FishNet
        if (InstanceFinder.TimeManager != null)
            InstanceFinder.TimeManager.OnTick += TimeManager_OnTick;
        // Включаем импуты только если мы хозяева этого персонажа
        if (IsOwner)
        {
            if (MoveAction != null) MoveAction.Enable();
            if (JumpAction != null) JumpAction.Enable();
            if (SprintAction != null) SprintAction.Enable();
            if (CrouchAction != null) CrouchAction.Enable();

            if (JumpAction != null) JumpAction.performed += OnJumpPerformed;
        }
    }
    public override void OnStopClient()
    {
        base.OnStopClient();
        // Обязательно отписываемся от тиков, чтобы не было утечек памяти!
        if (InstanceFinder.TimeManager != null)
            InstanceFinder.TimeManager.OnTick -= TimeManager_OnTick;
        // Выключаем и отписываемся при отключении
        if (IsOwner)
        {
            MoveAction.Disable();
            JumpAction.Disable();
            SprintAction.Disable();
            CrouchAction.Disable();
            JumpAction.performed -= OnJumpPerformed;
        }
    }
    // Графика и Логика (Привязана к FPS: 60, 100, 144 - неважно)
    private void Update()
    {
        if (!IsOwner) return; //Только игрок может управлять персонажем
        //🖱️ Детекция мыши
        if (Mouse.current != null)
        {
            // Мы получаем изменение положения мыши для текущего кадра (30f - компенсация медленного движения мыши)
            Vector2 mouseDelta = LookAction.ReadValue<Vector2>() * (mouseSensivity * 30f) * Time.deltaTime;
            // Поворачиваем туловище влево и вправо
            transform.Rotate(Vector3.up * mouseDelta.x);
            // Наклоняем камеру вверх и вниз
            // Насколько наклонить камеру, отняв координапты мыши за текущий кадр, ограничив 85 градусами
            _cameraRotationX = Mathf.Clamp(_cameraRotationX - mouseDelta.y, -85f, 85f); // Насколько сдвинуть камеру, отняв координапты мыши за текущий кадр, ограничив 85 градусами
            playerCamera.transform.localRotation = Quaternion.Euler(_cameraRotationX, 0f, 0f);
        }
        //🎥 Настраиваем положение камеры, учитывая эффект дыхания 
        if (playerCamera != null)
        {
            // Если двигаемся, качаем камеру быстрее. Если стоим - это плавное дыхание
            float currentSpeed = _speed > 0.1f ? breathSpeed * _speed / 2f : breathSpeed;
            float currentAmount = _speed > 0.1f ? breathAmplitude * _speed / 2f : breathAmplitude;
            _timer += Time.deltaTime * currentSpeed;
            // Сдвигаем камеру по синусоиде вверх-вниз относительно дефолтной высоты
            Vector3 newPos = playerCamera.transform.localPosition;
            newPos.y = _defaultY + Mathf.Sin(_timer) * currentAmount;
            playerCamera.transform.localPosition = newPos;
        }
        //🎬 ПЕРЕДАЧА ПАРАМЕТРОВ В АНИМАТОР
        if (anim != null && _playerSensor != null)
        {
            // ВМЕСТО кнопок смотрим на реальное направление движения персонажа!
            // Вместо создания нового вектора, просто используем текущую скорость Rigidbody прямо в формуле
            float directionDot = Vector3.Dot(new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z), transform.forward);
            // Создаем локальную скорость, чтобы глобальную не трогать
            _speedForAnim = _speed;
            // Если результат меньше нуля, значит физическое тело движется СПИНОЙ вперед
            if (directionDot < -0.05f)
                _speedForAnim = -_speedForAnim; // Задом идем медленнее
            //❯❯❯❯🎬 Передаем скорость игрока в аниматор
            if (CrouchAction.IsPressed())
                anim.SetFloat(_SpeedHash, _speedForAnim, 0.1f, Time.deltaTime);
            else
                anim.SetFloat(_SpeedHash, _speedForAnim);
            // Передаем положение стоит/присяд
            anim.SetBool(_IsCrouchedHash, CrouchAction.IsPressed());
            //🔎 🌍 💧 💨ДЕТЕКЦИЯ СЛОЕВ (ВОДА, ЗЕМЛЯ и тд)
            //🔎 Если на уровне груди игрока изменения по сравнению с предыдущим сохраненным флагом
            if (_playerSensor.LastPlayerChest.Value != _playerSensor.CurrentPlayerChest.Value)
            {
                anim.SetBool(_BuoyantHash, _playerSensor.CurrentPlayerChest.Value == PlayerState.Water);
                _playerSensor.LastPlayerChest.Value = _playerSensor.CurrentPlayerChest.Value;
            }
            //🔎 Если на уровне ног изменения по сравнению с предыдущим сохраненным флагом
            if (_playerSensor.LastPlayerLegs.Value != _playerSensor.CurrentPlayerLegs.Value)
            {
                // Выключаем то, из чего вышли
                switch (_playerSensor.LastPlayerLegs.Value)
                {
                    case PlayerState.Water: anim.SetBool(_WaterHash, false); break;
                    case PlayerState.Ground: anim.SetBool(_GroundHash, false); break;
                }
                // Включаем то, куда пришли
                switch (_playerSensor.CurrentPlayerLegs.Value)
                {
                    case PlayerState.Water: anim.SetBool(_WaterHash, true); break;
                    case PlayerState.Ground: anim.SetBool(_GroundHash, true); break;
                }
                _playerSensor.LastPlayerLegs.Value = _playerSensor.CurrentPlayerLegs.Value;
            }
            //🔎 Если под ногами изменения по сравнению с предыдущим сохраненным флагом (для анимации Landing)
            if (_playerSensor.LastPlayerBelowLegs != _playerSensor.CurrentPlayerBelowLegs)
            {
                anim.SetBool(_LandingHash, _playerSensor.CurrentPlayerBelowLegs.Value == PlayerState.Ground);
                _playerSensor.LastPlayerBelowLegs.Value = _playerSensor.CurrentPlayerBelowLegs.Value;
            }
            //🕹 Детекция нажатий
            if (_playerSensor.CurrentPlayerLegs.Value != PlayerSensor.PlayerState.Water) //Ноги НЕ в воде
                HandleCrouch();
        }
    }
    //🌐 Синхронизация тиков по сети между клиентами
    private void TimeManager_OnTick()
    {
        if (IsOwner)
        {
            //🕹️ Клиент-владелец собирает данные со своей клавиатуры
            MoveData md = new MoveData(
                MoveAction.ReadValue<Vector2>(),
                _jumpWasPressed,
                SprintAction.IsPressed(),
                CrouchAction.IsPressed()
            );
            //❌🤸 Сбрасываем флаг прыжка, так как мы его уже упаковали в этот тик
            _jumpWasPressed = false;
            //⏩🖥️ Вызываем репликацию (клиент выполнит локально, а пакет улетит на сервер)
            ReplicateMove(md);
        }
        else if (IsServerInitialized)
            //⏩🖥️ Если это сервер — он тупо ждет пакеты от клиентов и симулирует их
            ReplicateMove(default);
    }
    [Replicate]
    //🖥️ Выполнение на локальном клиенте
    private void ReplicateMove(MoveData md, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        // Мгновенно меняем высоту физического коллайдера на сервере и клиенте под текущий тик
        SetColliderHeightDirect(md.CrouchPressed);
        //❯❯❯❯ Считаем текущую горизонтальную скорость тела игрока
        Vector3 horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        _speed = horizontalVelocity.magnitude;
        //🤸 Сетевая обработка прыжка на основе данных из md
        if (md.JumpPressed && _playerSensor.CurrentPlayerLegs.Value != PlayerSensor.PlayerState.Water)
            _rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
        //🔄 Вычисляем физическое направление движения
        Vector3 moveDir = (transform.forward * md.MoveInput.y + transform.right * md.MoveInput.x).normalized;
        //🐌❓ Определяем, что мы сейчас делаем: разгоняемся (WASD зажат) или тормозим (WASD отпущен)
        float currentRate = (md.MoveInput.magnitude > 0) ? acceleration : deceleration;
        //🐌 Если мы НЕ на земле и НЕ в воде — срезаем силу торможения/разгона (для более длительного прыжка)
        if (_playerSensor.CurrentPlayerLegs.Value == PlayerSensor.PlayerState.Nothing) currentRate *= airControlFactor;
        //❯❯❯❯🏃 Устанавливаем стандартную скорость ходьбыаем
        float currentSpeed = walkSpeed;
        //❯❯❯❯🔼 Сохраняем текущую вертикальную скорость
        float targetVelocityY = _rb.linearVelocity.y;
        //💧 Если мы в воде
        if (_playerSensor.CurrentPlayerLegs.Value == PlayerSensor.PlayerState.Water)
        {
            switch (md.CrouchPressed, md.SprintPressed)
            {
                case (true, _):
                    Debug.Log("In water press ctrl");
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
        //🌍 Если мы на суше
        else
        {
            currentSpeed = (md.CrouchPressed, md.SprintPressed, md.MoveInput.y >= 0) switch
            {
                (true, _, _) => crouchSpeed, // Нажат присяд, все равно на спринт и направление движения
                (false, true, true) => sprintSpeed, // Не нажат присяд, нажат спринт, движение вперед
                _ => walkSpeed    // В остальных случаях
            };
        }
        //🐌🔙 Окончательное урезание скорости в воде и на суше, если движение задом
        if (md.MoveInput.y < -0.1f) currentSpeed *= 0.5f;
        //❯❯❯❯✅ Идеальная горизонтальная скорость, которую хочет получить игрок прямо сейчас
        Vector3 normalVelocity = moveDir * currentSpeed;
        //❯❯❯❯🚶 ПЛАВНО подтягиваем ТЕКУЩУЮ скорость Rigidbody для плавности старта после нажатия клавиш
        float targetVelocityX = Mathf.MoveTowards(_rb.linearVelocity.x, normalVelocity.x, currentRate * Time.fixedDeltaTime);
        float targetVelocityZ = Mathf.MoveTowards(_rb.linearVelocity.z, normalVelocity.z, currentRate * Time.fixedDeltaTime);
        //🚶 Применяем скорость нативно
        if (_playerSensor.CurrentPlayerLegs.Value == PlayerSensor.PlayerState.Water && !md.CrouchPressed)
            // Если мы в воде и НЕ жмем Ctrl (погружение) — мы позволяем физике Архимеда свободно двигать игрока вверх/вниз!
            // Мы перезаписываем ТОЛЬКО горизонтальную скорость X и Z, не трогая Y!
            _rb.linearVelocity = new Vector3(targetVelocityX, _rb.linearVelocity.y, targetVelocityZ);
        else
            // Если мы на суше ИЛИ насильно топим себя через Ctrl — применяем targetVelocityY как обычно
            _rb.linearVelocity = new Vector3(targetVelocityX, targetVelocityY, targetVelocityZ);
        //🔨 Намертво центрируем модель внутри капсулы, блокируя любые инерционные сдвиги
        if (anim != null)
            // Намертво центрируем модель внутри капсулы, блокируя любые инерционные сдвиги
            anim.transform.localPosition = new Vector3(0f, -0.01f, 0f);
    }
    [Reconcile]
    //📋 Метод проверки игрока: позиция, угол поворота, скороость
    private void ReconcileMove(ReconcileData rd, Channel channel = Channel.Unreliable)
    {
        transform.position = rd.Position;
        transform.rotation = rd.Rotation;
        _rb.linearVelocity = rd.LinearVelocity;
    }
    public override void CreateReconcile()
    {
        base.CreateReconcile();
        // Создаем структуру сверки данных прямо здесь
        ReconcileData rd = new ReconcileData(transform.position, transform.rotation, _rb.linearVelocity);
        // И сразу же вызываем наш метод сверки, как того требует ошибка
        ReconcileMove(rd);
    }

    //🦵🏻🎥 Нажатие ctrl, камера визуально плавно опускается на клиенте
    private void HandleCrouch()
    {
        // Определяем целевую высоту камеры на основе ввода
        float targetCameraY = CrouchAction.IsPressed() ? (crouchHeight * cameraHeightRatio) : (standHeight * cameraHeightRatio);
        // Плавное сглаживание, независимое от FPS
        float lerpFactor = 1f - Mathf.Exp(-crouchSmoothTime * Time.deltaTime);
        _defaultY = Mathf.Lerp(_defaultY, targetCameraY, lerpFactor);
    }
    //🦵🏻🌐 Нажатие ctrl, коллайдер игрока жестко опускается на клиенте, а сервер подхватывает и опускает у себя тоже
    private void SetColliderHeightDirect(bool isCrouched)
    {
        // Выставляем жесткую высоту коллайдера в зависимости от флага из MoveData
        float targetHeight = isCrouched ? crouchHeight : standHeight;
        _capsuleCollider.height = targetHeight;
        // Центрируем капсулу, чтобы подошва всегда была на нуле
        _capsuleCollider.center = new Vector3(0f, targetHeight/2f, 0f);
    }
    //🤸 Детекция прыжка
    private void OnJumpPerformed(InputAction.CallbackContext context)
    {
        // Если ноги не в воде — взводим флаг прыжка для следующего сетевого тика
        if (_playerSensor != null && _playerSensor.CurrentPlayerLegs.Value != PlayerSensor.PlayerState.Water)
            _jumpWasPressed = true;
    }
}
