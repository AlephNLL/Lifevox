using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class SimulateCells : MonoBehaviour
{
    public Texture2D texture;

    [Header("Simulation")]
    public int tps = 20;
    public bool pauseSimulation = false;

    [Header("World Properties")]
    public float airDensity = 1.225f;
    public float roomTemperature = 24f;

    // Cache de variables
    public float[,] temperatureMap;
    private HashSet<Vector2> pixelMap;
    private Dictionary<Color, Cell> cellCache;
    private HashSet<Vector2> asleepCells;
    private System.Random rand;
    private Dictionary<Vector2, CellNeighbors> neighborsCache = new Dictionary<Vector2, CellNeighbors>();
    private int cacheFrame = 0;
    private int textureWidth, textureHeight;
    private Dictionary<Color, float> densityCache = new Dictionary<Color, float>();
    private Dictionary<Color, GravityType> gravityCache = new Dictionary<Color, GravityType>();
    private int cleanUpCounter = 0;
    private const int CLEANUP_INTERVAL = 60;

    // ZONAS ACTIVAS - VARIABLES NUEVAS
    private HashSet<Vector2Int> activeZones = new HashSet<Vector2Int>();
    private Dictionary<Vector2Int, int> zoneActivityLevel = new Dictionary<Vector2Int, int>();
    private const int ZONE_SIZE = 10; 
    private const int MAX_ZONE_ACTIVITY = 5;

    // Constantes
    private const float ABSOLUTE_ZERO = -273.15f;
    private const float STEFAN_BOLTZMANN = 5.670374419e-11f; // Escalado para g en lugar de kg
    private const float PIXEL_MASS = 0.001f; // 1 gramo por píxel

    // Ajustar estos valores para mayor velocidad
    [Header("Heat Transfer Settings")]
    public float thermalConductivity = 0.1f;
    public float heatTransferRate = 0.05f;
    public float radiativeTransferRate = .01f; // Aumentado para radiación
    private const float MAX_TEMP_CHANGE_PER_TICK = 100f; // Más alto
    private const float MIN_TEMP_DIFFERENCE = 0.1f;

    private void Start()
    {
        textureWidth = texture.width;
        textureHeight = texture.height;
        InitializeSimulation();
        StartCoroutine(Simulate());
    }

    private void InitializeSimulation()
    {
        temperatureMap = new float[texture.width, texture.height];
        pixelMap = new HashSet<Vector2>();
        cellCache = new Dictionary<Color, Cell>();
        asleepCells = new HashSet<Vector2>();
        rand = new System.Random();

        InitializeTemperatureAndTexture();
    }

    private void InitializeTemperatureAndTexture()
    {
        for (int x = 0; x < texture.width; x++)
        {
            for (int y = 0; y < texture.height; y++)
            {
                temperatureMap[x, y] = roomTemperature;

                if (IsBorder(x, y))
                {
                    texture.SetPixel(x, y, Color.white);
                }
                else if (texture.GetPixel(x, y) != Color.black)
                {
                    texture.SetPixel(x, y, Color.black);
                }
            }
        }
        texture.Apply();
    }

    private void Update()
    {
        CleanupCaches();
        ProcessMouseTooltip();
    }

    private void ProcessMouseTooltip()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Vector2 pixelUV = hit.textureCoord;
            pixelUV.x *= texture.width;
            pixelUV.y *= texture.height;

            string tooltip = GetCellTooltip((int)pixelUV.x, (int)pixelUV.y);
            TooltipScreenSpaceUI.SetText_Static(tooltip);
        }
    }

    private string GetCellTooltip(int x, int y)
    {
        Cell cell = GetCellObject(texture.GetPixel(x, y));
        int temperature = Mathf.FloorToInt(temperatureMap[x, y]);

        return cell != null ? $"{cell.cellName} ({temperature}C)" : $"";
    }

    private IEnumerator Simulate()
    {
        var waitForTick = new WaitForSeconds(1f / tps);

        while (!pauseSimulation)
        {
            yield return waitForTick;
            ExecuteSimulationTick();
        }
    }

    private void ExecuteSimulationTick()
    {
        Color[,] colorGrid = UpdateCells();
        UpdateTemperatureMap(colorGrid);
        texture.Apply();
    }

    private Color[,] UpdateCells()
    {
        if (pixelMap.Count <= 0) return new Color[0, 0];

        Color[,] colorGrid = InitializeColorGrid();
        HashSet<Vector2> newPixelMap = new HashSet<Vector2>();

        // USAR CÓDIGO EXISTENTE para shuffle
        var cellsList = pixelMap.ToList();

        // Shuffle manual sin métodos extra
        for (int i = 0; i < cellsList.Count; i++)
        {
            int randomIndex = rand.Next(i, cellsList.Count);
            Vector2 temp = cellsList[i];
            cellsList[i] = cellsList[randomIndex];
            cellsList[randomIndex] = temp;
        }

        // PROCESAR SOLO CELDAS EN ZONAS ACTIVAS
        foreach (Vector2 cell in cellsList)
        {
            int x = (int)cell.x;
            int y = (int)cell.y;
            Vector2Int zone = GetZoneFromPosition(x, y);

            if (IsZoneActive(zone) || ShouldActivateZone(x, y, colorGrid))
            {
                ProcessCell(cell, colorGrid, newPixelMap);
            }
            else
            {
                // Mantener celda inactiva en el pixelMap sin procesar
                newPixelMap.Add(cell);
            }
        }

        pixelMap = newPixelMap;
        ApplyColorGridToTexture(colorGrid);

        // Actualizar estado de zonas
        UpdateZoneActivity();

        return colorGrid;
    }

    private Vector2Int GetZoneFromPosition(int x, int y)
    {
        return new Vector2Int(x / ZONE_SIZE, y / ZONE_SIZE);
    }

    private bool IsZoneActive(Vector2Int zone)
    {
        return activeZones.Contains(zone);
    }

    private bool ShouldActivateZone(int x, int y, Color[,] colorGrid)
    {
        Cell cell = GetCellObject(colorGrid[x, y]);
        if (cell == null) return false;

        // CRITERIOS MÁS ESTRICTOS para activación

        // 1. Celda puede moverse Y realmente tiene espacio para moverse
        if (!cell.isStatic)
        {
            if (CanCellMove(x, y, cell, colorGrid))
                return true;
        }

        // 2. Temperatura MUY extrema (no solo 20°C de diferencia)
        if (Mathf.Abs(temperatureMap[x, y] - roomTemperature) > 100f)
            return true;

        // 3. Cambio de fase inminente (muy cerca del punto de transición)
        if (IsNearPhaseTransition(cell, temperatureMap[x, y]))
            return true;

        // 4. Zona adyacente con ALTA actividad (no cualquier zona adyacente)
        Vector2Int zone = GetZoneFromPosition(x, y);
        if (HasHighActivityAdjacentZone(zone))
            return true;

        return false;
    }

    private bool CanCellMove(int x, int y, Cell cell, Color[,] colorGrid)
    {
        // Verificar movimiento real, no solo espacios vacíos
        CellNeighbors neighbors = GetCellNeighbors(x, y, colorGrid);

        switch (cell.gravityType)
        {
            case GravityType.CompactSolid:
            case GravityType.PowderSolid:
                return neighbors.down == Color.black ||
                       (neighbors.downDensity < cell.cellDensity && neighbors.down != Color.white);

            case GravityType.Liquid:
                return neighbors.down == Color.black ||
                       neighbors.downLeft == Color.black ||
                       neighbors.downRight == Color.black ||
                       (neighbors.downDensity < cell.cellDensity && neighbors.down != Color.white);

            case GravityType.Gas:
                return neighbors.down == Color.black ||
                       neighbors.up == Color.black ||
                       neighbors.left == Color.black ||
                       neighbors.right == Color.black;
        }

        return false;
    }

    private bool IsNearPhaseTransition(Cell cell, float temperature)
    {
        if (cell.hotTransitionCell != null && temperature > cell.hotPhasePoint - 10f)
            return true;

        if (cell.coldTransitionCell != null && temperature < cell.coldPhasePoint + 10f)
            return true;

        return false;
    }

    private bool HasHighActivityAdjacentZone(Vector2Int zone)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                Vector2Int adjacentZone = new Vector2Int(zone.x + dx, zone.y + dy);
                if (zoneActivityLevel.TryGetValue(adjacentZone, out int activity) && activity >= 3)
                    return true;
            }
        }
        return false;
    }

    private void MarkZoneActive(Vector2Int zone)
    {
        activeZones.Add(zone);

        // Incrementar nivel de actividad
        if (!zoneActivityLevel.ContainsKey(zone))
            zoneActivityLevel[zone] = 0;

        zoneActivityLevel[zone] = MAX_ZONE_ACTIVITY;
    }

    private void UpdateZoneActivity()
    {
        var zonesToRemove = new List<Vector2Int>();
        var activityToReduce = new List<Vector2Int>();

        foreach (var zone in activeZones)
        {
            if (zoneActivityLevel.ContainsKey(zone))
            {
                zoneActivityLevel[zone] -= 1;

                if (zoneActivityLevel[zone] <= 0)
                {
                    zonesToRemove.Add(zone);
                }
            }
            else
            {
                zonesToRemove.Add(zone);
            }
        }

        // Remover zonas inactivas
        foreach (var zone in zonesToRemove)
        {
            activeZones.Remove(zone);
            zoneActivityLevel.Remove(zone);
        }

        // DECAIMIENTO PERIÓDICO MÁS FRECUENTE
        if (Time.frameCount % 15 == 0) // Cada 15 frames en lugar de 30
        {
            foreach (var zone in activeZones.ToList()) // Usar ToList para evitar modificación durante iteración
            {
                if (zoneActivityLevel.TryGetValue(zone, out int activity) && activity < 3)
                {
                    zoneActivityLevel[zone] = Mathf.Max(1, activity - 1);
                }
            }
        }
    }

    private Color[,] InitializeColorGrid()
    {
        Color[,] grid = new Color[texture.width, texture.height];
        for (int x = 0; x < texture.width; x++)
        {
            for (int y = 0; y < texture.height; y++)
            {
                grid[x, y] = texture.GetPixel(x, y);
            }
        }
        return grid;
    }

    private void ApplyColorGridToTexture(Color[,] colorGrid)
    {
        for (int x = 0; x < texture.width; x++)
        {
            for (int y = 0; y < texture.height; y++)
            {
                texture.SetPixel(x, y, colorGrid[x, y]);
            }
        }
    }

    private void ProcessCell(Vector2 updateableCell, Color[,] colorGrid, HashSet<Vector2> newPixelMap)
    {
        int x = (int)updateableCell.x;
        int y = (int)updateableCell.y;

        if (!IsProcessableCell(colorGrid[x, y], x, y)) return;

        Cell cell = GetCellObject(colorGrid[x, y]);
        Color pixel = colorGrid[x, y];
        float temp = temperatureMap[x, y];

        if(TryChemicalReactions(x, y, colorGrid, newPixelMap))
        {
            MarkZoneActive(GetZoneFromPosition(x, y));
            return;
        }

        if (TryPhaseTransition(cell, x, y, colorGrid, updateableCell, newPixelMap, temp))
        {
            MarkZoneActive(GetZoneFromPosition(x, y));
            return;
        }

        if (!cell.isStatic)
        {
            bool moved = ProcessCellMovement(cell, x, y, colorGrid, newPixelMap, pixel, temp, updateableCell);
            if (moved)
            {
                MarkZoneActive(GetZoneFromPosition(x, y));
            }
        }

        if (!newPixelMap.Contains(updateableCell))
        {
            newPixelMap.Add(updateableCell);
        }
    }

    private bool ProcessCellMovement(Cell cell, int x, int y, Color[,] colorGrid, HashSet<Vector2> newPixelMap, Color pixel, float temp, Vector2 updateableCell)
    {
        CellNeighbors neighbors = GetCellNeighbors(x, y, colorGrid);
        bool moved = false;

        switch (cell.gravityType)
        {
            case GravityType.CompactSolid:
                moved = TryMoveSolid(cell, x, y, colorGrid, newPixelMap, pixel, temp, neighbors);
                break;
            case GravityType.PowderSolid:
                moved = TryMovePowder(cell, x, y, colorGrid, newPixelMap, pixel, temp, neighbors, updateableCell);
                break;
            case GravityType.Liquid:
                moved = TryMoveLiquid(cell, x, y, colorGrid, newPixelMap, pixel, temp, neighbors);
                break;
            case GravityType.Gas:
                moved = TryMoveGas(cell, x, y, colorGrid, newPixelMap, pixel, temp, neighbors);
                break;
        }

        return moved;
    }

    private bool IsProcessableCell(Color cellColor, int x, int y)
    {
        return CellTypeDatabase.instance.colors.ContainsValue(cellColor) &&
               !IsBorder(x, y);
    }

    private bool IsBorder(int x, int y)
    {
        return x == 0 || x == texture.width - 1 || y == 0 || y == texture.height - 1;
    }

    private bool TryPhaseTransition(Cell cell, int x, int y, Color[,] colorGrid, Vector2 updateableCell, HashSet<Vector2> newPixelMap, float temp)
    {
        if (cell.hotTransitionCell != null && cell.hotPhasePoint < temp)
        {
            colorGrid[x, y] = cell.hotTransitionCell.color;
            newPixelMap.Add(updateableCell);
            return true;
        }

        if (cell.coldTransitionCell != null && cell.coldPhasePoint > temp)
        {
            colorGrid[x, y] = cell.coldTransitionCell.color;
            newPixelMap.Add(updateableCell);
            return true;
        }

        return false;
    }

    private bool TryChemicalReactions(int x, int y, Color[,] colorGrid, HashSet<Vector2> newPixelMap)
    {
        Cell currentCell = GetCellObject(colorGrid[x, y]);
        if (currentCell == null) return false;

        bool reactionOcurred = false;
        // Solo verificar reacciones para esta celda si está en la base de datos
        if (!ChemicalReactionsDatabase.instance.reactionCache.ContainsKey(currentCell)) return false;

        float currentTemp = temperatureMap[x, y];

        // Verificar todas las reacciones que involucran esta celda
        foreach (var reaction in ChemicalReactionsDatabase.instance.reactionCache[currentCell])
        {
            if (CanReactionOccur(x, y, reaction, colorGrid, currentTemp))
            {
                ExecuteReaction(x, y, reaction, colorGrid, newPixelMap);
                reactionOcurred = true;
                break; // Solo una reacción por celda por frame
            }
        }

        return reactionOcurred;
    }

    private bool CanReactionOccur(int x, int y, ChemicalReaction reaction, Color[,] colorGrid, float temperature)
    {
        // Verificar temperatura
        if (temperature < reaction.minTemperature || temperature > reaction.maxTemperature)
            return false;

        // Verificar probabilidad
        if (Random.value > reaction.probability)
            return false;

        // Contar reactivos en el área circundante
        Dictionary<Cell, int> reactantCounts = new Dictionary<Cell, int>();

        // Buscar en un radio de 1-2 píxeles
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                if (dx == 0 && dy == 0) continue; // Saltar la celda central

                int checkX = x + dx;
                int checkY = y + dy;

                if (!IsInBounds(new Vector2(checkX, checkY))) continue;

                Cell neighborCell = GetCellObject(colorGrid[checkX, checkY]);
                if (neighborCell != null && reaction.reactants.Contains(neighborCell))
                {
                    if (!reactantCounts.ContainsKey(neighborCell))
                        reactantCounts[neighborCell] = 0;
                    reactantCounts[neighborCell]++;
                }
            }
        }

        // Verificar si tenemos suficientes reactivos
        for (int i = 0; i < reaction.reactants.Length; i++)
        {
            Cell reactant = reaction.reactants[i];
            int required = reaction.reactantCounts[i];

            if (!reactantCounts.ContainsKey(reactant) || reactantCounts[reactant] < required)
                return false;
        }

        return true;
    }

    private void ExecuteReaction(int x, int y, ChemicalReaction reaction, Color[,] colorGrid, HashSet<Vector2> newPixelMap)
    {
        // 1. Reemplazar celda central con producto
        colorGrid[x, y] = reaction.product.color;
        temperatureMap[x, y] += reaction.energyReleased; // Ajustar temperatura

        // 2. Encontrar y consumir reactivos cercanos
        Dictionary<Cell, int> reactantsToConsume = new Dictionary<Cell, int>();
        for (int i = 0; i < reaction.reactants.Length; i++)
        {
            reactantsToConsume[reaction.reactants[i]] = reaction.reactantCounts[i];
        }

        // Consumir reactivos (empezando por los más cercanos)
        for (int distance = 1; distance <= 2 && reactantsToConsume.Count > 0; distance++)
        {
            for (int dx = -distance; dx <= distance; dx++)
            {
                for (int dy = -distance; dy <= distance; dy++)
                {
                    if (Mathf.Abs(dx) != distance && Mathf.Abs(dy) != distance) continue;

                    int checkX = x + dx;
                    int checkY = y + dy;

                    if (!IsInBounds(new Vector2(checkX, checkY))) continue;

                    Cell neighborCell = GetCellObject(colorGrid[checkX, checkY]);
                    if (neighborCell != null && reactantsToConsume.ContainsKey(neighborCell))
                    {
                        // Consumir este reactivo
                        colorGrid[checkX, checkY] = Color.black; // Vacío
                        reactantsToConsume[neighborCell]--;

                        if (reactantsToConsume[neighborCell] <= 0)
                            reactantsToConsume.Remove(neighborCell);

                        // Actualizar pixelMap
                        newPixelMap.Add(new Vector2(checkX, checkY));

                        if (reactantsToConsume.Count == 0) break;
                    }
                }
                if (reactantsToConsume.Count == 0) break;
            }
            if (reactantsToConsume.Count == 0) break;
        }

        // 3. Activar zonas alrededor
        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dy = -3; dy <= 3; dy++)
            {
                MarkZoneActive(GetZoneFromPosition(x + dx, y + dy));
            }
        }

        Debug.Log($"Reacción: {reaction.reactionName} en ({x},{y})");
    }


    private CellNeighbors GetCellNeighbors(int x, int y, Color[,] colorGrid)
    {
        Vector2 pos = new Vector2(x, y);

        // RESET CACHE CADA FRAME - Esto es crucial
        if (cacheFrame != Time.frameCount)
        {
            neighborsCache.Clear();
            cacheFrame = Time.frameCount;
        }

        // RETORNAR CACHEADO SI EXISTE
        if (neighborsCache.TryGetValue(pos, out CellNeighbors cached))
            return cached;

        // CALCULAR SOLO SI NO ESTÁ EN CACHE
        var neighbors = CalculateNeighbors(x, y, colorGrid);
        neighborsCache[pos] = neighbors;
        return neighbors;
    }

    private CellNeighbors CalculateNeighbors(int x, int y, Color[,] colorGrid)
    {
        var neighbors = new CellNeighbors();

        // PRE-CALCULAR TODOS LOS BOUNDS UNA VEZ
        bool hasLeft = x > 0;
        bool hasRight = x < textureWidth - 1;
        bool hasDown = y > 0;
        bool hasUp = y < textureHeight - 1;

        // VECINOS DIRECTOS (siempre necesarios)
        if (hasDown)
        {
            neighbors.down = colorGrid[x, y - 1];
            neighbors.downDensity = GetCellDensity(neighbors.down);
        }

        if (hasUp)
        {
            neighbors.up = colorGrid[x, y + 1];
            neighbors.upDensity = GetCellDensity(neighbors.up);
            neighbors.upCellGravity = GetUpCellGravity(neighbors.up);
        }

        if (hasLeft)
        {
            neighbors.left = colorGrid[x - 1, y];
            neighbors.leftDensity = GetCellDensity(neighbors.left);
        }

        if (hasRight)
        {
            neighbors.right = colorGrid[x + 1, y];
            neighbors.rightDensity = GetCellDensity(neighbors.right);
        }

        // VECINOS DIAGONALES (solo para materiales que los usan)
        if (hasLeft)
        {
            if (hasDown)
            {
                neighbors.downLeft = colorGrid[x - 1, y - 1];
                neighbors.downLeftDensity = GetCellDensity(neighbors.downLeft);
            }

            if (hasUp)
            {
                neighbors.upLeft = colorGrid[x - 1, y + 1];
            }
        }

        if (hasRight)
        {
            if (hasDown)
            {
                neighbors.downRight = colorGrid[x + 1, y - 1];
                neighbors.downRightDensity = GetCellDensity(neighbors.downRight);
            }

            if (hasUp)
            {
                neighbors.upRight = colorGrid[x + 1, y + 1];
            }
        }

        return neighbors;
    }

    private struct CellNeighbors
    {
        public Color down, downLeft, downRight, up, upLeft, upRight, left, right;
        public float downDensity, downLeftDensity, downRightDensity, upDensity, leftDensity, rightDensity;
        public GravityType upCellGravity;
    }

    private bool TryMoveSolid(Cell cell, int x, int y, Color[,] colorGrid, HashSet<Vector2> newPixelMap, Color pixel, float temp, CellNeighbors n)
    {
        if (CanMoveTo(n.down, n.downDensity, cell.cellDensity))
        {
            MoveCell(x, y, x, y - 1, colorGrid, newPixelMap, pixel, temp);
            return true;
        }
        return false;
    }

    private bool TryMovePowder(Cell cell, int x, int y, Color[,] colorGrid, HashSet<Vector2> newPixelMap, Color pixel, float temp, CellNeighbors n, Vector2 updateableCell)
    {
        // Verificar si debe dormirse primero
        if (ShouldSleep(cell, n, updateableCell))
        {
            if (!asleepCells.Contains(updateableCell))
                asleepCells.Add(updateableCell);
        }

        // Movimiento hacia abajo
        if (CanMoveTo(n.down, n.downDensity, cell.cellDensity))
        {
            MoveCell(x, y, x, y - 1, colorGrid, newPixelMap, pixel, temp);
            WakeNeighbors(x, y);
            return true;
        }

        // Si está dormido, no intenta movimiento lateral
        if (asleepCells.Contains(updateableCell)) return false;

        // Movimiento diagonal
        bool movedLeft = rand.Next(0, 2) == 0;
        bool movedDiagonally = movedLeft ?
            TryMoveDiagonal(x, y, x - 1, y - 1, n.downLeft, n.downLeftDensity, cell, colorGrid, newPixelMap, pixel, temp) :
            TryMoveDiagonal(x, y, x + 1, y - 1, n.downRight, n.downRightDensity, cell, colorGrid, newPixelMap, pixel, temp);

        if (movedDiagonally)
        {
            WakeNeighbors(x, y);
            return true;
        }

        return false;
    }

    private bool TryMoveLiquid(Cell cell, int x, int y, Color[,] colorGrid, HashSet<Vector2> newPixelMap, Color pixel, float temp, CellNeighbors n)
    {
        // Movimiento vertical prioritario
        if (n.down == Color.black || n.downDensity < cell.cellDensity)
        {
            MoveCell(x, y, x, y - 1, colorGrid, newPixelMap, pixel, temp);
            return true;
        }
        if (n.downLeft == Color.black || n.downLeftDensity < cell.cellDensity)
        {
            MoveCell(x, y, x - 1, y - 1, colorGrid, newPixelMap, pixel, temp);
            return true;
        }
        if (n.downRight == Color.black || n.downRightDensity < cell.cellDensity)
        {
            MoveCell(x, y, x + 1, y - 1, colorGrid, newPixelMap, pixel, temp);
            return true;
        }

        // Movimiento horizontal por dispersión
        return TryLiquidDispersion(cell, x, y, colorGrid, newPixelMap, pixel, temp);
    }

    private bool TryMoveGas(Cell cell, int x, int y, Color[,] colorGrid, HashSet<Vector2> newPixelMap, Color pixel, float temp, CellNeighbors n)
    {
        if (rand.Next(1, cell.floatSlowMod) != 1) return false;

        // Movimiento vertical aleatorio
        if (rand.Next(0, 2) == 0)
        {
            if (n.down == Color.black && cell.cellDensity >= airDensity || n.downDensity < cell.cellDensity)
            {
                MoveCell(x, y, x, y - 1, colorGrid, newPixelMap, pixel, temp);
                return true;
            }
        }
        else
        {
            if (n.up == Color.black && cell.cellDensity <= airDensity || n.upDensity > cell.cellDensity)
            {
                if (n.upCellGravity != GravityType.Gas && n.upCellGravity != GravityType.Liquid)
                {
                    newPixelMap.Add(new Vector2(x, y));
                    return false;
                }
                MoveCell(x, y, x, y + 1, colorGrid, newPixelMap, pixel, temp);
                return true;
            }
        }

        // Movimiento horizontal
        if (rand.Next(0, 2) == 0)
        {
            if (n.left == Color.black || n.leftDensity < cell.cellDensity)
            {
                MoveCell(x, y, x - 1, y, colorGrid, newPixelMap, pixel, temp);
                return true;
            }
        }
        else
        {
            if (n.right == Color.black || n.rightDensity < cell.cellDensity)
            {
                MoveCell(x, y, x + 1, y, colorGrid, newPixelMap, pixel, temp);
                return true;
            }
        }

        return false;
    }

    // Métodos auxiliares
    private bool TryMoveDiagonal(int fromX, int fromY, int toX, int toY, Color targetColor, float targetDensity,
                           Cell cell, Color[,] colorGrid, HashSet<Vector2> newPixelMap, Color pixel, float temp)
    {
        if (targetColor == Color.black || targetDensity < cell.cellDensity)
        {
            MoveCell(fromX, fromY, toX, toY, colorGrid, newPixelMap, pixel, temp);
            return true;
        }
        return false;
    }

    private bool TryLiquidDispersion(Cell cell, int x, int y, Color[,] colorGrid, HashSet<Vector2> newPixelMap, Color pixel, float temp)
    {
        bool moveLeft = rand.Next(0, 2) == 0;
        Vector2 availableCell = FindAvailableCell(cell, x, y, colorGrid, moveLeft, newPixelMap);

        if (availableCell == Vector2.zero) return false;

        MoveCell(x, y, (int)availableCell.x, (int)availableCell.y, colorGrid, newPixelMap, pixel, temp);
        return true;
    }

    private Vector2 FindAvailableCell(Cell cell, int x, int y, Color[,] colorGrid, bool moveLeft, HashSet<Vector2> newPixelMap)
    {
        Vector2 lastAvailable = Vector2.zero;
        int direction = moveLeft ? -1 : 1;

        for (int i = 1; i <= cell.dispersionRate; i++)
        {
            int checkX = x + (i * direction);
            if (checkX < 0 || checkX >= texture.width) break;

            if (colorGrid[checkX, y] == Color.black || GetCellDensity(colorGrid[checkX, y]) < cell.cellDensity)
            {
                lastAvailable = new Vector2(checkX, y);
            }
            else
            {
                if (i == 1) newPixelMap.Add(new Vector2(x, y));
                break;
            }
        }

        return lastAvailable;
    }

    private bool ShouldSleep(Cell cell, CellNeighbors n, Vector2 updateableCell)
    {
        bool cantMove = !CanMoveTo(n.down, n.downDensity, cell.cellDensity) &&
                       !CanMoveTo(n.downLeft, n.downLeftDensity, cell.cellDensity) &&
                       !CanMoveTo(n.downRight, n.downRightDensity, cell.cellDensity);

        return cantMove && rand.Next(1, cell.compactness) == 1;
    }

    private bool CanMoveTo(Color targetColor, float targetDensity, float cellDensity)
    {
        return targetColor == Color.black || targetDensity < cellDensity;
    }

    private void MoveCell(int fromX, int fromY, int toX, int toY, Color[,] colorGrid,
                      HashSet<Vector2> newPixelMap, Color pixel, float temp)
    {
        Color targetColor = colorGrid[toX, toY];
        float targetTemp = temperatureMap[toX, toY];

        colorGrid[fromX, fromY] = targetColor;
        colorGrid[toX, toY] = pixel;

        if (targetColor != Color.black)
        {
            temperatureMap[fromX, fromY] = targetTemp;
            temperatureMap[toX, toY] = temp;
        }
        else
        {
            temperatureMap[fromX, fromY] = roomTemperature; 
            temperatureMap[toX, toY] = temp;               
        }

        newPixelMap.Add(new Vector2(toX, toY));
        if (targetColor != Color.black)
            newPixelMap.Add(new Vector2(fromX, fromY));
    }

    private void WakeNeighbors(int x, int y)
    {
        asleepCells.Remove(new Vector2(x - 1, y));
        asleepCells.Remove(new Vector2(x + 1, y));
    }

    void UpdateTemperatureMap(Color[,] colorGrid)
    {
        foreach (Vector2 cell in pixelMap)
        {
            int x = (int)cell.x;
            int y = (int)cell.y;
            Vector2Int zone = GetZoneFromPosition(x, y);

            // Procesar si la zona está activa O si la celda tiene razón para activarse
            if (IsZoneActive(zone) || ShouldActivateForHeatTransfer(x, y, colorGrid))
            {
                ProcessCellHeatTransfer(cell, colorGrid);

                // Si no estaba activa, activarla ahora
                if (!IsZoneActive(zone))
                {
                    MarkZoneActive(zone);
                }
            }
        }
    }

    private bool ShouldActivateForHeatTransfer(int x, int y, Color[,] colorGrid)
    {
        Cell cell = GetCellObject(colorGrid[x, y]);
        CellNeighbors n = GetCellNeighbors(x,y,colorGrid);
        if (cell == null) return false;

        // Activar para transferencia de calor si:
        // 1. Temperatura muy diferente a los vecinos
        // 2. Cerca de un cambio de fase
        // 3. Material con alta conductividad térmica

        return (temperatureMap[x, y] != roomTemperature) ||
               IsNearPhaseTransition(cell, temperatureMap[x, y]) ||
               CanNeighborsTransferHeat(x,y);
    }

    // DEBUG: Visualizar zonas activas (opcional)
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        if (texture == null) return;

        // Obtener el renderer para calcular bounds exactos
        Renderer renderer = GetComponent<Renderer>();
        if (renderer == null) return;

        Bounds bounds = renderer.bounds;
        Vector3 textureMin = bounds.min;
        Vector3 textureSize = bounds.size;

        // Calcular tamaño de píxel en unidades mundiales
        float pixelSizeX = textureSize.x / textureWidth;
        float pixelSizeY = textureSize.y / textureHeight;

        foreach (Vector2Int zone in activeZones)
        {
            // Convertir zona a coordenadas mundiales exactas
            Vector3 zoneMin = new Vector3(
                textureMin.x + (zone.x * ZONE_SIZE * pixelSizeX),
                textureMin.y + (zone.y * ZONE_SIZE * pixelSizeY),
                textureMin.z
            );

            Vector3 zoneSize = new Vector3(
                ZONE_SIZE * pixelSizeX,
                ZONE_SIZE * pixelSizeY,
                1
            );

            Vector3 zoneCenter = zoneMin + zoneSize * 0.5f;
            if(zoneActivityLevel[zone] >=3) Gizmos.color = Color.red;
            else Gizmos.color = Color.blue;

            Gizmos.DrawWireCube(zoneCenter, zoneSize);
        }
    }
    private void ProcessCellHeatTransfer(Vector2 cell, Color[,] colorGrid)
    {
        int x = (int)cell.x;
        int y = (int)cell.y;

        if (!TryGetValidCell(x, y, colorGrid, out Cell currentCell)) return;

        CellNeighbors neighbors = GetCellNeighbors(x, y, colorGrid);
        float totalHeatChange = CalculateNeighborHeatTransfer(x, y, currentCell, neighbors);
        //totalHeatChange += CalculateRadiativeHeat(x, y, currentCell, neighbors);

        UpdateCellTemperature(x, y, totalHeatChange);
    }

    private bool TryGetValidCell(int x, int y, Color[,] cellGrid, out Cell currentCell)
    {
        currentCell = IsInBounds(new Vector2(x, y)) ? GetCellObject(cellGrid[x, y]) : null;

        if (!currentCell)
        {
            //Debug.Log($"Invalid cell at: ({x}, {y})");
            return false;
        }
        return true;
    }

    private bool CanNeighborsTransferHeat(int x, int y)
    {
        float current = temperatureMap[x, y];
        float right = temperatureMap[x + 1, y];
        float left = temperatureMap[x - 1, y];
        float up = temperatureMap[x , y + 1];
        float down = temperatureMap[x , y - 1];

        return current != right || current != left || current != up || current != down;    
    }

    private float CalculateNeighborHeatTransfer(int x, int y, Cell currentCell, CellNeighbors neighbors)
    {
        float totalHeatChange = 0;

        if (neighbors.right != null)
        {
            Cell rightCell = GetCellObject(neighbors.right);
            if (rightCell != null)
                totalHeatChange -= CalculateDirectionalHeatTransfer(x, y, x + 1, y, currentCell, rightCell);
        }

        if (neighbors.left != null)
        {
            Cell leftCell = GetCellObject(neighbors.left);
            if (leftCell != null)
                totalHeatChange -= CalculateDirectionalHeatTransfer(x, y, x - 1, y, currentCell, leftCell);
        }

        if (neighbors.up != null)
        {
            Cell upCell = GetCellObject(neighbors.up);
            if (upCell != null)
                totalHeatChange -= CalculateDirectionalHeatTransfer(x, y, x, y + 1, currentCell, upCell);
        }

        if (neighbors.down != null)
        {
            Cell downCell = GetCellObject(neighbors.down);
            if (downCell != null)
                totalHeatChange -= CalculateDirectionalHeatTransfer(x, y, x, y - 1, currentCell, downCell);
        }

        return totalHeatChange;
    }

    private float CalculateDirectionalHeatTransfer(int sourceX, int sourceY, int targetX, int targetY,
                                                 Cell sourceCell, Cell targetCell)
    {
        // targetCell ya está verificado que no es null
        return CalculateHeatTransfer(
            temperatureMap[sourceX, sourceY],
            temperatureMap[targetX, targetY],
            sourceCell.thermalConductivity,
            targetCell.thermalConductivity,
            sourceCell.specificHeat,
            targetCell.specificHeat
        );
    }

    private float CalculateRadiativeHeat(int x, int y, Cell currentCell, CellNeighbors neighbors)
    {
        bool hasAllNeighbors = (neighbors.right != null && neighbors.left != null &&
                               neighbors.up != null && neighbors.down != null);

        if (!hasAllNeighbors)
        {
            return CalculateRadiativeHeat(temperatureMap[x, y], roomTemperature, currentCell.emissivity);
        }
        return 0f;
    }

    private void UpdateCellTemperature(int x, int y, float heatChange)
    {
        temperatureMap[x, y] += heatChange;
        temperatureMap[x, y] = Mathf.Max(temperatureMap[x, y], ABSOLUTE_ZERO);
    }

    public float CalculateHeatTransfer(float tempSource, float tempTarget,
                                  float k1, float k2, float cp1 = 1f, float cp2 = 1f)
    {
        float temperatureDifference = tempSource - tempTarget;
        if (Mathf.Abs(temperatureDifference) < MIN_TEMP_DIFFERENCE)
            return 0;

        // 1. Conductividad efectiva (media geométrica)
        float kEffective = Mathf.Sqrt(k1 * k2);

        // 2. Capacidad térmica efectiva - AHORA en J/(g·K)
        float cpEffective = (cp1 + cp2) / 2000f;

        // 3. FACTOR DE ESCALA CRÍTICO - Compensar la división por 1000
        float scaleFactor = 1f; 

        // 4. Ley de Fourier ajustada
        float baseTransfer = kEffective * temperatureDifference;

        // 5. Transferencia final con compensación
        float heatTransfer = heatTransferRate * baseTransfer * scaleFactor / cpEffective;

        // 6. Límites para estabilidad
        float maxTransfer = Mathf.Min(
            Mathf.Abs(temperatureDifference) * 0.05f, // Más permisivo
            MAX_TEMP_CHANGE_PER_TICK
        );

        heatTransfer = Mathf.Clamp(heatTransfer, -maxTransfer, maxTransfer);

        return heatTransfer;
    }

    // Radiación mejorada con ley de Stefan-Boltzmann simplificada
    public float CalculateRadiativeHeat(float currentTemp, float ambientTemp, float emissivity)
    {
        if (Mathf.Abs(currentTemp - ambientTemp) < MIN_TEMP_DIFFERENCE) return 0;

        float currentTempK = currentTemp + 273.15f;
        float ambientTempK = ambientTemp + 273.15f;

        // Ley de Stefan-Boltzmann escalada para gramos
        float radiativePower = emissivity * STEFAN_BOLTZMANN *
                              (Mathf.Pow(currentTempK, 4) - Mathf.Pow(ambientTempK, 4));

        // Convertir a cambio de temperatura (ahora para masa de 1g)
        float deltaTemperature = radiativePower * radiativeTransferRate / PIXEL_MASS;

        // Límites más permisivos
        float maxRadiation = Mathf.Abs(currentTemp - ambientTemp) * 0.1f;
        return Mathf.Clamp(deltaTemperature, -maxRadiation, maxRadiation);
    }

    public void UpdatePixelMap(Vector2 coords)
    {
        if (IsInBounds(coords))
            pixelMap.Add(coords);
    }

    bool IsInBounds(Vector2 pos)
    {
        return pos.x > 0 && pos.x < texture.width - 1 && pos.y > 0 && pos.y < texture.height - 1;
    }

    private GravityType GetUpCellGravity(Color upColor)
    {
        if (upColor == Color.white) return GravityType.CompactSolid;

        if (gravityCache.TryGetValue(upColor, out GravityType cachedGravity))
            return cachedGravity;

        Cell upCell = GetCellObject(upColor);
        GravityType gravity = upCell?.gravityType ?? GravityType.Gas;
        gravityCache[upColor] = gravity;

        return gravity;
    }

    Cell GetCellObject(Color color)
    {
        if (color == Color.black || color == Color.white)
            return null;

        if (cellCache.TryGetValue(color, out Cell cachedCell))
            return cachedCell;

        if (CellTypeDatabase.instance.colors.ContainsValue(color) &&
            CellTypeDatabase.instance._colors.TryGetValue(color, out int index))
        {
            // cellObjects es un array, usar .Length
            if (index >= 0 && index < CellTypeDatabase.instance.cellObjects.Length)
            {
                Cell cell = CellTypeDatabase.instance.cellObjects[index];
                cellCache[color] = cell;
                return cell;
            }
        }

        cellCache[color] = null;
        return null;
    }

    float GetCellDensity(Color color)
    {
        if (color == Color.white) return float.MaxValue;
        if (color == Color.black) return airDensity;

        // Cache de densidad por color
        if (densityCache.TryGetValue(color, out float cachedDensity))
            return cachedDensity;

        Cell cell = GetCellObject(color);
        float density = cell?.cellDensity ?? airDensity;
        densityCache[color] = density;

        return density;
    }

    private void CleanupCaches()
    {
        cleanUpCounter++;
        if (cleanUpCounter >= CLEANUP_INTERVAL)
        {
            // Limpiar caches de colores que ya no se usan
            var activeColors = new HashSet<Color>();
            for (int x = 0; x < textureWidth; x++)
            {
                for (int y = 0; y < textureHeight; y++)
                {
                    activeColors.Add(texture.GetPixel(x, y));
                }
            }

            // Remover entradas de cache para colores inactivos
            densityCache = densityCache.Where(kvp => activeColors.Contains(kvp.Key))
                                      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            gravityCache = gravityCache.Where(kvp => activeColors.Contains(kvp.Key))
                                      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            cleanUpCounter = 0;
        }
    }
}

