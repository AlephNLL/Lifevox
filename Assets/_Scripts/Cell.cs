using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MyBox;

public enum StatesOfMatter
{
    solid,
    liquid,
    gas,
    plasma,
    boseEinsteinCondensate
}

public enum GravityType
{
    CompactSolid,
    PowderSolid,
    Liquid,
    Gas
}

public enum Conditions
{
    temperature,
    quantity
}

[CreateAssetMenu(fileName = "New Cell")]
public class Cell : ScriptableObject
{
    public Color32 color;

    [Header("Info")]
    [Tooltip("The name of this particle.")]
    public string cellName;

    [Tooltip("The state of matter of this particle.")]
    public StatesOfMatter state;

    [Header("GravityType")]

    [Tooltip("Enable this to make the particle static and unaffected by any form of force.")]
    public bool isStatic = false;

    public GravityType gravityType;

    [Tooltip("The density of the particle in kg/m^3 at 0ºC")]
    public float cellDensity;

    [Header("Temperature")]
    [Tooltip("The temperature in Celsius at which a phase transition is triggered")]
    public float coldPhasePoint;
    public float hotPhasePoint;

    public float thermalConductivity;
    public float specificHeat;
    public float emissivity;

    [Tooltip("The cell this cell transforms into when cold phase transition")]
    public Cell coldTransitionCell;
    [Tooltip("The cell this cell transforms into when hot phase transition")]
    public Cell hotTransitionCell;

    [ConditionalField("state", false, StatesOfMatter.solid)]
    [Header("SolidProperties")]
    [Tooltip("The probability a cell sleeps when not moving")]
    public int compactness = 1;

    [ConditionalField("state", false, StatesOfMatter.liquid)]
    [Header("LiquidProperties")]
    [Tooltip("The number of cells a liquid can check to the side to move.")]
    public int dispersionRate = 1;

    [ConditionalField("state", false, StatesOfMatter.gas)]
    [Header("GasProperties")]
    [Tooltip("The chance every frame for your particle to rise.")]
    public int floatSlowMod = 10;

}
