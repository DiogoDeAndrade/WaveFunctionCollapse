using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

public class WFCTest : MonoBehaviour
{
    [Header("Source")]
    public Texture2D image;
    public Vector2Int ngramSize = new Vector2Int(3, 3);
    public bool wrapX;
    public bool wrapY;
    public bool symmetricX;
    public bool symmetricY;
    public bool rotate;
    [Header("Output")]
    public bool       randomSeed;
    public int        maxSteps = 1000;
    [HideIf("randomSeed")]
    public int        seed;
    public Vector2Int size = new Vector2Int(256, 256);

    class CorpusElem
    {
        public int          weights;
        public float        weightLogWeights;
        public Color[]      color;
        public List<int>[]  allowedLinks;

        public bool IsSame(Color[] c)
        {
            for (int i = 0; i < color.Length; i++)
            {
                if (c[i] != color[i]) return false;
            }

            return true;
        }        
    }

    List<CorpusElem> corpus;

    class State
    {
        public bool[]   state;
        public float    sumOfOnes;
        public float    sumOfWeights;
        public float    sumOfWeightLogWeights;
        public Color    color;
        public float    entropy;
        public int[][]  compatibilityCount;

        public float[] GetDistribution(List<CorpusElem> corpus)
        {
            float[] ret = new float[corpus.Count];

            for (int i = 0; i < corpus.Count; i++)
            {
                if (state[i]) ret[i] = corpus[i].weights;
                else ret[i] = 0;
            }

            return ret;
        }
    };

    // Working data
    State[]                 states;
    float                   sumOfWeights;
    float                   sumOfWeightLogWeights;
    float                   startingEntropy;
    System.Random           rndGenerator;
    Stack<(int,int,int)>    ban;

    Vector2Int[] directions = { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1) };
    int[] oppositeDirection = { 2, 3, 0, 1 };

    void Start()
    {

    }

    void Update()
    {

    }


    [Button("Generate Corpus")]
    void GenerateCorpus()
    {
        if (!image)
        {
            Debug.LogError("Source image is required!");
            return;
        }

        if (!image.isReadable)
        {
            Debug.LogError("Source image is not readable!");
            return;
        }

        corpus = new List<CorpusElem>();

        var pixels = image.GetPixels();
        int sx, sy;

        if (wrapX)
        {
            sx = image.width;
        }
        else
        {
            sx = ngramSize.x * Mathf.FloorToInt(image.width / (float)ngramSize.x) - 1;
        }

        if (wrapY)
        {
            sy = image.height;
        }
        else
        {
            sy = ngramSize.y * Mathf.FloorToInt(image.height / (float)ngramSize.y) - 1;
        }

        for (int y = 0; y < sy; y++)
        {
            for (int x = 0; x < sx; x++)
            {
                Color[] c = new Color[ngramSize.x * ngramSize.y];

                for (int dx = 0; dx < ngramSize.x; dx++)
                {
                    for (int dy = 0; dy < ngramSize.y; dy++)
                    {
                        int xx = (x + dx) % image.width;
                        int yy = (y + dy) % image.height;

                        c[dx + dy * ngramSize.x] = pixels[xx + yy * image.width];
                    }
                }

                AddNGram(c, true);
            }
        }

        // Generate propagator
        GeneratePropagator();

        sumOfWeights = 0;
        sumOfWeightLogWeights = 0;

        // Compute log weights
        foreach (var item in corpus)
        {
            item.weightLogWeights = item.weights * Mathf.Log(item.weights);
            sumOfWeights += item.weights;
            sumOfWeightLogWeights += item.weightLogWeights;
        }

        startingEntropy = Mathf.Log(sumOfWeights) - sumOfWeightLogWeights / sumOfWeights;

        Debug.Log($"Total ngrams = {corpus.Count}");
    }

    void GeneratePropagator()
    {
        for (int i = 0; i < corpus.Count; i++)
        {
            corpus[i].allowedLinks = new List<int>[4];

            for (int d = 0; d < directions.Length; d++)
            {
                corpus[i].allowedLinks[d] = new List<int>();

                for (int j = 0; j < corpus.Count; j++)
                {
                    if (CanMatch(i, j, directions[d])) corpus[i].allowedLinks[d].Add(j);
                }
            }
        }
    }

    bool CanMatch(int t1, int t2, Vector2Int dir)
    {
        int xmin = dir.x < 0 ? 0 : dir.x;
        int xmax = dir.x < 0 ? dir.x + ngramSize.x : ngramSize.x;
        int ymin = dir.y < 0 ? 0 : dir.y;
        int ymax = dir.y < 0 ? dir.y + ngramSize.y : ngramSize.y;

        var p1 = corpus[t1].color;
        var p2 = corpus[t2].color;

        for (int y = ymin; y < ymax; y++)
        {
            for (int x = xmin; x < xmax; x++)
            {
                if (p1[x + ngramSize.x * y] != p2[x - dir.x + ngramSize.x * (y - dir.y)])
                    return false;
            }
        }
        return true;
    }

    void AddNGram(Color[] ngram, bool addDerived)
    {
        if (corpus == null) corpus = new List<CorpusElem>();

        // Check if this one already exists on the corpus
        foreach (var item in corpus)
        {
            if (item.IsSame(ngram))
            {
                item.weights += 1;
                return;
            }
        }

        corpus.Add(new CorpusElem { color = ngram, weights = 1 });

        if (addDerived)
        {
            if (symmetricX) AddNGram(SymmetryX(ngram), false);
            if (symmetricY) AddNGram(SymmetryY(ngram), false);
            if (rotate)
            {
                var tmp = Rot90(ngram);
                AddNGram(tmp, false);
                tmp = Rot90(tmp);
                AddNGram(tmp, false);
                tmp = Rot90(tmp);
                AddNGram(tmp, false);
            }
        }
    }

    Color[] SymmetryX(Color[] ngram)
    {
        Color[] ret = new Color[ngramSize.x * ngramSize.y];

        for (int y = 0; y < ngramSize.y; y++)
        {
            for (int x = 0; x < ngramSize.x; x++)
            {
                ret[x + y * ngramSize.x] = ngram[ngramSize.x - x - 1 + y * ngramSize.x];
            }
        }

        return ret;
    }

    Color[] SymmetryY(Color[] ngram)
    {
        Color[] ret = new Color[ngramSize.x * ngramSize.y];

        for (int y = 0; y < ngramSize.y; y++)
        {
            for (int x = 0; x < ngramSize.x; x++)
            {
                ret[x + y * ngramSize.x] = ngram[x + (ngramSize.y - y - 1) * ngramSize.x];
            }
        }

        return ret;
    }

    Color[] Rot90(Color[] ngram)
    {
        Color[] ret = new Color[ngramSize.x * ngramSize.y];

        for (int y = 0; y < ngramSize.y; y++)
        {
            for (int x = 0; x < ngramSize.x; x++)
            {
                ret[x + y * ngramSize.x] = ngram[ngramSize.x - 1 - y + x * ngramSize.x];
            }
        }

        return ret;
    }

    [Button("Show Corpus")]
    void ShowCorpus()
    {
        int corpusCount = corpus.Count;
        int corpusCountX = Mathf.CeilToInt(Mathf.Sqrt(corpusCount)) + 1;
        int corpusCountY = Mathf.CeilToInt(Mathf.Sqrt(corpusCount)) + 1;

        int width = corpusCountX * (ngramSize.x + 1);
        int height = corpusCountY * (ngramSize.y + 1);

        Color[] colors = new Color[width * height];

        int dx = 0;
        int dy = 0;
        for (int i = 0; i < corpus.Count; i++)
        {
            var c = corpus[i];

            for (int x = 0; x < ngramSize.x; x++)
            {
                for (int y = 0; y < ngramSize.y; y++)
                {
                    colors[(dx + x) + (dy + y) * width] = c.color[x + y * ngramSize.x];
                }
            }

            dx += (ngramSize.x + 1);
            if (dx + ngramSize.x + 1 >= width)
            {
                dx = 0;
                dy += (ngramSize.y + 1);
            }
        }

        Texture2D texture = new Texture2D(width, height);
        texture.SetPixels(colors);

        texture.filterMode = FilterMode.Point;

        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 1);

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
    }

    [Button("Generate Output")]
    void GenerateOutput()
    {
        if (corpus == null)
        {
            GenerateCorpus();
        }

        Setup();

        int steps = 0;

        while ((steps < maxSteps) || (maxSteps == 0))
        {
            int res = RunStep();
            switch (res)
            {
                case -1:
                    Debug.LogError("Contradiction: can't complete!");
                    break;
                case 0:
                    break;
                case 1:
                    Debug.Log("Completed!");
                    break;
                default:
                    break;
            }
            if (res != 0) break;

            steps++;
        }

        if ((steps >= maxSteps) && (maxSteps > 0))
        {
            Debug.LogError("Too many steps...");
        }

        UpdateGraphics();
    }

    [Button("Setup")]
    void SetupAndUpdate()
    {
        Setup();
        UpdateGraphics();
    }

    private void Setup()
    {
        if (randomSeed)
        {
            int rndSeed = Random.Range(-int.MaxValue, int.MaxValue);
            rndGenerator = new System.Random(rndSeed);
            Debug.Log("Seed=" + rndSeed);
        }
        else
        {
            rndGenerator = new System.Random(seed);
            Debug.Log("Seed=" + seed);
        }

        states = new State[size.x * size.y];
        ban = new Stack<(int, int, int)>();

        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                int index = x + y * size.x;
                states[index] = new State()
                {
                    state = new bool[corpus.Count],
                    sumOfOnes = corpus.Count,
                    sumOfWeights = sumOfWeights,
                    sumOfWeightLogWeights = sumOfWeightLogWeights,
                    color = Color.black,
                    entropy = startingEntropy,
                    compatibilityCount = new int[corpus.Count][]
                };
                for (int i = 0; i < corpus.Count; i++)
                {
                    states[index].state[i] = true;
                    states[index].compatibilityCount[i] = new int[directions.Length];
                    for (int d = 0; d < directions.Length; d++)
                    {
                        states[index].compatibilityCount[i][d] = corpus[i].allowedLinks[oppositeDirection[d]].Count;
                    }
                }
            }
        }

        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                UpdateColor(x, y);
            }
        }
    }

    private void UpdateColor(int x, int y)
    {
        int cx = Mathf.FloorToInt(ngramSize.x / 2);
        int cy = Mathf.FloorToInt(ngramSize.y / 2);

        int index = x + y * size.x;

        states[index].color = Color.black;

        for (int i = 0; i < states[index].state.Length; i++)
        {
            if (states[index].state[i])
            {
                states[index].color += corpus[i].color[cx + cy * ngramSize.y] / states[index].sumOfOnes;
            }
        }
    }

    void UpdateGraphics()
    {
        Color[] output = new Color[size.x * size.y];
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                int index = x + y * size.x;
                output[x + y * size.x] = states[index].color;
            }
        }

        Texture2D texture = new Texture2D(size.x, size.y);
        texture.SetPixels(output);

        texture.filterMode = FilterMode.Point;

        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size.x, size.y), new Vector2(0.5f, 0.5f), 1);

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
    }

    [Button("Run Step")]
    void RunStepAndUpdate()
    {
        int stepResult = RunStep();
        switch (stepResult)
        {
            case -1:
                Debug.LogError("Contradiction: can't complete!");
                break;
            case 0:
                break;
            case 1:
                Debug.Log("Completed!");
                break;
            default:
                break;
        }
        UpdateGraphics();
    }

    private int RunStep()
    {
        float       minEntropy = float.MaxValue;
        Vector2Int  current = new Vector2Int(-1, -1);
        State       s;

        // Find minimum entropy
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                if (OnBoundary(x, y)) continue;
                    
                s = states[x + y * size.x];

                if (s.sumOfOnes == 0)
                {
                    return -1;
                }

                float entropy = s.entropy;
                if ((s.sumOfOnes > 1) && (entropy < minEntropy))
                {
                    float noise = 1E-6f * (float)rndGenerator.NextDouble();
                    if (entropy + noise < minEntropy)
                    { 
                        current = new Vector2Int(x, y);
                        minEntropy = entropy + noise;
                    }
                }
            }
        }

        if (current.x == -1)
        {
            // No low entropy found
            return 1;
        }

        s = states[current.x + current.y * size.x];

        float[] distribution = s.GetDistribution(corpus);
        int r = distribution.Random(rndGenerator);

        // Place the ngram in the image
        SetState(current.x, current.y, r);

        // Run propagation
        Propagate();

        return 0;
    }

    int SelectRandomForState(int x, int y)
    {
        List<int>   possibleNGrams = new List<int>();

        int idx = x + y * size.x;

        for (int i = 0; i < corpus.Count; i++)
        {
            if (states[idx].state[i]) possibleNGrams.Add(i);
        }

        return possibleNGrams[rndGenerator.Range(0, possibleNGrams.Count)];
    }

    void SetState(int x, int y, int ngram)
    {
        for (int i = 0; i < corpus.Count; i++)
        {
            if (i != ngram)
            {
                Ban(x, y, i);
            }
        }
    }

    void Ban(int x, int y, int corpusElem)
    {
        int idx = x + y * size.x;
        var s = states[idx];

        if (!s.state[corpusElem]) return;

        s.state[corpusElem] = false;
        for (int d = 0; d < directions.Length; d++) s.compatibilityCount[corpusElem][d] = 0;

        s.sumOfOnes--;
        s.sumOfWeights -= corpus[corpusElem].weights;
        s.sumOfWeightLogWeights -= corpus[corpusElem].weightLogWeights;

        s.entropy = Mathf.Log(s.sumOfWeights) - s.sumOfWeightLogWeights / s.sumOfWeights;

        UpdateColor(x, y);

        ban.Push((x, y, corpusElem));
    }

    void Propagate()
    {
        while (ban.Count > 0)
        {
            int srcX, srcY, corpusIndex;

            (srcX,srcY,corpusIndex) = ban.Pop();

            for (int d = 0; d < directions.Length; d++)
            {
                var direction = directions[d];

                int x = srcX + direction.x;
                int y = srcY + direction.y;

                // Check if it is on the boundary - could wrap around
                if (OnBoundary(x, y)) continue;

                var allowedElems = corpus[corpusIndex].allowedLinks[d];
                var compat = states[x + y * size.x].compatibilityCount;

                foreach (var al in allowedElems)
                {
                    int[] comp = compat[al];

                    comp[d]--;
                    if (comp[d] == 0) Ban(x, y, al);
                }
            }
        }
    }

    bool OnBoundary(int x, int y)
    {
        if (x < 0) return true;
        if (y < 0) return true;
        if (x + ngramSize.x > size.x + 1) return true;
        if (y + ngramSize.y > size.y + 1) return true;

        return false;
    }
}
