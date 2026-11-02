using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public enum Tools
{
    Draw,
    Heat
}
public class ToolRack : MonoBehaviour
{
    public Texture2D texture;

    public Cell selectedCell;

    public Tools selectedTool;

    public int brushRad;

    public float heatIntensity;

    SimulateCells sim;

    private void Start()
    {
        sim = GetComponent<SimulateCells>();
    }
    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            if (selectedTool == Tools.Draw)
            {
                brushRad = Mathf.Clamp(brushRad + 1, 1, 20);
            }
            else
            {
                heatIntensity = Mathf.Clamp(heatIntensity + 10, 1, 2000);
            }
        }
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            if(selectedTool == Tools.Draw)
            {
                brushRad = Mathf.Clamp(brushRad - 1, 1, 20);
            }
            else
            {
                heatIntensity = Mathf.Clamp(heatIntensity - 10, 1, 2000);
            }
        }
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            selectedTool = selectedTool == Tools.Draw ? Tools.Heat : Tools.Draw;
        }
        if (Input.GetMouseButton(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector2 pixelUV = hit.textureCoord;
                pixelUV.x *= texture.width;
                pixelUV.y *= texture.height;

                switch (selectedTool)
                {
                    case Tools.Draw:
                        if (hit.transform.gameObject.layer != LayerMask.NameToLayer("UI"))
                        {
                            texture.DrawCircle(selectedCell.color, (int)pixelUV.x, (int)pixelUV.y, brushRad);

                            float rSquared = brushRad * brushRad;
                            for (int u = (int)pixelUV.x - brushRad; u < (int)pixelUV.x + brushRad + 1; u++)
                            {
                                for (int v = (int)pixelUV.y - brushRad; v < (int)pixelUV.y + brushRad + 1; v++)
                                {
                                    if (((int)pixelUV.x - u) * ((int)pixelUV.x - u) + ((int)pixelUV.y - v) * ((int)pixelUV.y - v) < rSquared)
                                    {
                                        sim.UpdatePixelMap(new Vector2(u,v));
                                    }
                                }
                            }
                        }
                        break;
                    case Tools.Heat:
                        if (hit.transform.gameObject.layer != LayerMask.NameToLayer("UI"))
                        {
                            float rSquared = brushRad * brushRad;
                            for (int u = (int)pixelUV.x - brushRad; u < (int)pixelUV.x + brushRad + 1; u++)
                            {
                                for (int v = (int)pixelUV.y - brushRad; v < (int)pixelUV.y + brushRad + 1; v++)
                                {
                                    if (((int)pixelUV.x - u) * ((int)pixelUV.x - u) + ((int)pixelUV.y - v) * ((int)pixelUV.y - v) < rSquared)
                                    {
                                        if(texture.GetPixel(u,v) == Color.black || texture.GetPixel(u, v) == Color.white) continue;
                                        sim.temperatureMap[u, v] += heatIntensity * Time.deltaTime;
                                    }
                                }

                            }
                        }
                        break;
                    default:
                        break;
                }
                
            }
        }

        if (Input.GetMouseButton(1))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector2 pixelUV = hit.textureCoord;
                pixelUV.x *= texture.width;
                pixelUV.y *= texture.height;

                switch (selectedTool)
                {
                    case Tools.Draw:
                        if (hit.transform.gameObject.layer != LayerMask.NameToLayer("UI"))
                        {
                            texture.DrawCircle(Color.black, (int)pixelUV.x, (int)pixelUV.y, brushRad);
                            float rSquared = brushRad * brushRad;
                            for (int u = (int)pixelUV.x - brushRad; u < (int)pixelUV.x + brushRad + 1; u++)
                            {
                                for (int v = (int)pixelUV.y - brushRad; v < (int)pixelUV.y + brushRad + 1; v++)
                                {
                                    if (((int)pixelUV.x - u) * ((int)pixelUV.x - u) + ((int)pixelUV.y - v) * ((int)pixelUV.y - v) < rSquared)
                                    {
                                        if (texture.GetPixel(u, v) == Color.white) continue;
                                        sim.temperatureMap[u, v] = sim.roomTemperature;
                                    }
                                }

                            }
                        }
                        break;
                    case Tools.Heat:
                        if (hit.transform.gameObject.layer != LayerMask.NameToLayer("UI"))
                        {
                            float rSquared = brushRad * brushRad;
                            for (int u = (int)pixelUV.x - brushRad; u < (int)pixelUV.x + brushRad + 1; u++)
                            {
                                for (int v = (int)pixelUV.y - brushRad; v < (int)pixelUV.y + brushRad + 1; v++)
                                {
                                    if (((int)pixelUV.x - u) * ((int)pixelUV.x - u) + ((int)pixelUV.y - v) * ((int)pixelUV.y - v) < rSquared)
                                    {
                                        if (texture.GetPixel(u, v) == Color.black || texture.GetPixel(u, v) == Color.white) continue;
                                        sim.temperatureMap[u, v] -= heatIntensity * Time.deltaTime;
                                        if (sim.temperatureMap[u, v] < -273.15f) sim.temperatureMap[u, v] = -273.15f;
                                    }
                                }

                            }
                        }
                        break;
                    default:
                        break;
                }
                
                //print($"({(int)pixelUV.x}, {(int)pixelUV.y})" + texture.GetPixel((int)pixelUV.x, (int)pixelUV.y));
            }
        }

        /*if (Input.GetKeyDown("s") && (Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.LeftControl)))
        {
            bytes = texture.EncodeToPNG();
            PlayerPrefs.SetInt("NumSaved", PlayerPrefs.GetInt("NumSaved", 0) + 1);

            File.WriteAllBytes(Application.dataPath + $"/../SavedPixelWorld.png", bytes);
        }*/
    }

    public static Texture2D LoadPNG(string filePath)
    {

        Texture2D tex = null;
        byte[] fileData;

        fileData = File.ReadAllBytes(filePath);
        tex = new Texture2D(340, 180);
        tex.LoadImage(fileData); //..this will auto-resize the texture dimensions.

        return tex;
    }
}

public static class Tex2DExtension
{
    public static Texture2D DrawCircle(this Texture2D tex, Color color, int x, int y, int radius = 3)
    {
        float rSquared = radius * radius;

        for (int u = x - radius; u < x + radius + 1; u++)
            for (int v = y - radius; v < y + radius + 1; v++)
                if ((x - u) * (x - u) + (y - v) * (y - v) < rSquared)
                    if(tex.GetPixel(u,v) == Color.black || (color == Color.black && tex.GetPixel(u, v) != Color.white)) tex.SetPixel(u, v, color);

        return tex;
    }
    public static List<Color> GetCircle(this Texture2D tex, int x, int y, int radius = 3)
    {
        float rSquared = radius * radius;
        List<Color> colors = new List<Color>();

        for (int u = x - radius; u < x + radius + 1; u++)
            for (int v = y - radius; v < y + radius + 1; v++)
                if ((x - u) * (x - u) + (y - v) * (y - v) < rSquared)
                    colors.Add(tex.GetPixel(u, v));

        return colors;
    }
}
