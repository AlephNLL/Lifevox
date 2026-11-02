using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ChemicalReaction
{
    public string reactionName;
    public Cell[] reactants;        // Partículas necesarias
    public int[] reactantCounts;    // Cantidad de cada reactivo
    public Cell product;           // Partícula resultante
    public float minTemperature;   // Temperatura mínima requerida
    public float maxTemperature;   // Temperatura máxima permitida
    public float probability = 1f; // Probabilidad de ocurrir (0-1)
    public float energyReleased;   // Energía liberada/absorbida
}

public class ChemicalReactionsDatabase : MonoBehaviour
{
    public static ChemicalReactionsDatabase instance;
    private void Awake()
    {
        instance = this;
    }

    public List<ChemicalReaction> reactions = new List<ChemicalReaction>();

    [HideInInspector]
    public Dictionary<Cell, List<ChemicalReaction>> reactionCache = new Dictionary<Cell, List<ChemicalReaction>>();

    void Start()
    {
        // Precargar reacciones en cache
        foreach (var reaction in reactions)
        {
            foreach (var reactant in reaction.reactants)
            {
                if (!reactionCache.ContainsKey(reactant))
                    reactionCache[reactant] = new List<ChemicalReaction>();
                reactionCache[reactant].Add(reaction);
            }
        }
    }
}
