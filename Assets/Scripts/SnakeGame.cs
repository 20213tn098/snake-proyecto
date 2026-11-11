using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Juego de Snake completo en un solo componente.
/// El tablero, la serpiente y la comida se generan por código;
/// la escena solo necesita este componente, una cámara ortográfica y los textos de UI.
/// </summary>
public class SnakeGame : MonoBehaviour
{
    private enum State { Ready, Playing, GameOver }

    [Header("Tablero")]
    [Min(5)] public int width = 20;
    [Min(5)] public int height = 15;

    [Header("Velocidad (segundos por paso)")]
    public float stepInterval = 0.15f;
    public float minStepInterval = 0.06f;
    public float speedUpPerFood = 0.004f;

    [Header("Colores")]
    public Color backgroundColor = new Color(0.07f, 0.09f, 0.11f);
    public Color borderColor = new Color(0.25f, 0.32f, 0.38f);
    public Color boardColor = new Color(0.11f, 0.14f, 0.17f);
    public Color headColor = new Color(0.55f, 0.95f, 0.45f);
    public Color bodyColor = new Color(0.30f, 0.75f, 0.35f);
    public Color foodColor = new Color(0.95f, 0.30f, 0.30f);

    [Header("UI")]
    public Text scoreText;
    public Text messageText;

    private const string HighScoreKey = "SnakeHighScore";

    private readonly List<Vector2Int> body = new List<Vector2Int>();
    private readonly List<SpriteRenderer> segments = new List<SpriteRenderer>();
    private readonly Queue<Vector2Int> inputQueue = new Queue<Vector2Int>();

    private Sprite squareSprite;
    private Transform food;
    private Vector2Int foodCell;
    private Vector2Int direction;
    private State state;
    private float timer;
    private float currentInterval;
    private int score;
    private int highScore;

    private void Awake()
    {
        squareSprite = CreateSquareSprite();
        highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        SetupUiFonts();
        BuildBoard();
        SetupCamera();
        ResetGame();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (state == State.GameOver)
        {
            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)
                ResetGame();
            return;
        }

        ReadDirectionInput(keyboard);

        if (state == State.Ready)
        {
            if (inputQueue.Count == 0) return;
            state = State.Playing;
            SetMessage("");
        }

        timer += Time.deltaTime;
        while (timer >= currentInterval && state == State.Playing)
        {
            timer -= currentInterval;
            Step();
        }
    }

    // ---------- Lógica ----------

    private void ReadDirectionInput(Keyboard keyboard)
    {
        if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) QueueDirection(Vector2Int.up);
        if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) QueueDirection(Vector2Int.down);
        if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) QueueDirection(Vector2Int.left);
        if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) QueueDirection(Vector2Int.right);
    }

    /// <summary>Encola un giro; ignora los giros de 180° y los repetidos.</summary>
    private void QueueDirection(Vector2Int dir)
    {
        if (inputQueue.Count >= 2) return;
        Vector2Int last = direction;
        foreach (Vector2Int queued in inputQueue) last = queued;
        if (dir == -last) return;
        if (dir == last && state == State.Playing) return;
        inputQueue.Enqueue(dir);
    }

    private void ResetGame()
    {
        body.Clear();
        inputQueue.Clear();

        Vector2Int start = new Vector2Int(width / 2, height / 2);
        for (int i = 0; i < 3; i++) body.Add(new Vector2Int(start.x - i, start.y));

        direction = Vector2Int.right;
        currentInterval = stepInterval;
        timer = 0f;
        score = 0;
        state = State.Ready;

        SpawnFood();
        Render();
        UpdateScoreText();
        SetMessage("Usa las flechas o WASD para empezar");
    }

    private void Step()
    {
        if (inputQueue.Count > 0) direction = inputQueue.Dequeue();

        Vector2Int newHead = body[0] + direction;
        bool eats = newHead == foodCell;

        if (newHead.x < 0 || newHead.x >= width || newHead.y < 0 || newHead.y >= height)
        {
            EndGame("¡Chocaste con la pared!");
            return;
        }

        // La cola se mueve en este paso, así que solo cuenta como obstáculo si la serpiente crece.
        int checkCount = eats ? body.Count : body.Count - 1;
        for (int i = 0; i < checkCount; i++)
        {
            if (body[i] == newHead)
            {
                EndGame("¡Te mordiste la cola!");
                return;
            }
        }

        body.Insert(0, newHead);
        if (eats)
        {
            score++;
            currentInterval = Mathf.Max(minStepInterval, currentInterval - speedUpPerFood);
            UpdateScoreText();
            if (!SpawnFood())
            {
                Render();
                EndGame("¡Ganaste! Llenaste el tablero");
                return;
            }
        }
        else
        {
            body.RemoveAt(body.Count - 1);
        }

        Render();
    }

    /// <summary>Coloca la comida en una celda libre. Devuelve false si no queda ninguna.</summary>
    private bool SpawnFood()
    {
        List<Vector2Int> free = new List<Vector2Int>(width * height);
        HashSet<Vector2Int> occupied = new HashSet<Vector2Int>(body);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!occupied.Contains(cell)) free.Add(cell);
            }

        if (free.Count == 0)
        {
            food.gameObject.SetActive(false);
            return false;
        }

        foodCell = free[Random.Range(0, free.Count)];
        food.gameObject.SetActive(true);
        food.localPosition = CellToLocal(foodCell);
        return true;
    }

    private void EndGame(string reason)
    {
        state = State.GameOver;
        if (score > highScore)
        {
            highScore = score;
            PlayerPrefs.SetInt(HighScoreKey, highScore);
            PlayerPrefs.Save();
        }
        UpdateScoreText();
        SetMessage(reason + "\nPuntos: " + score + "\nPresiona ESPACIO para reiniciar");
    }

    // ---------- Vista ----------

    private void Render()
    {
        while (segments.Count < body.Count)
            segments.Add(CreateSquare("Segmento", bodyColor, 0.9f, 2));

        for (int i = 0; i < segments.Count; i++)
        {
            bool active = i < body.Count;
            segments[i].gameObject.SetActive(active);
            if (!active) continue;
            segments[i].transform.localPosition = CellToLocal(body[i]);
            segments[i].color = i == 0 ? headColor : bodyColor;
        }
    }

    private void BuildBoard()
    {
        SpriteRenderer border = CreateSquare("Borde", borderColor, 1f, 0);
        border.transform.localScale = new Vector3(width + 0.5f, height + 0.5f, 1f);

        SpriteRenderer board = CreateSquare("Tablero", boardColor, 1f, 1);
        board.transform.localScale = new Vector3(width, height, 1f);

        food = CreateSquare("Comida", foodColor, 0.7f, 2).transform;
    }

    private void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.orthographic = true;
        cam.backgroundColor = backgroundColor;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.transform.position = new Vector3(transform.position.x, transform.position.y, -10f);

        float vertical = height / 2f + 2.5f; // margen para el marcador
        float horizontal = (width / 2f + 1f) / cam.aspect;
        cam.orthographicSize = Mathf.Max(vertical, horizontal);
    }

    private void SetupUiFonts()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (scoreText != null && scoreText.font == null) scoreText.font = font;
        if (messageText != null && messageText.font == null) messageText.font = font;
    }

    private void UpdateScoreText()
    {
        if (scoreText != null) scoreText.text = "Puntos: " + score + "    Récord: " + highScore;
    }

    private void SetMessage(string message)
    {
        if (messageText != null) messageText.text = message;
    }

    private Vector3 CellToLocal(Vector2Int cell)
    {
        return new Vector3(cell.x - (width - 1) / 2f, cell.y - (height - 1) / 2f, 0f);
    }

    private SpriteRenderer CreateSquare(string objectName, Color color, float size, int sortingOrder)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(size, size, 1f);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = squareSprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    private static Sprite CreateSquareSprite()
    {
        Texture2D texture = new Texture2D(4, 4) { filterMode = FilterMode.Point };
        Color[] pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
    }
}
