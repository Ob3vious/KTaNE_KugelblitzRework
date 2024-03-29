﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Rnd = UnityEngine.Random;

public class KugelblitzLobby
{
    private List<KugelblitzScript> _members = new List<KugelblitzScript>();
    private List<IKugelblitzStageManager> _quirks;
    private byte _startingAngle;
    private bool _isOpen = true;

    private List<VoidLinkage> _linkages = new List<VoidLinkage>();

    private LobbyContentBuilder _content;
    private string _id;

    private KugelblitzColor _color;

    private string _input = "";
    public string ExpectedInput { get; private set; }
    private bool _solvable = false;
    private KugelblitzScript _lastInteract = null;

    private bool _solved = false;

    KMAudio.KMAudioRef _audioRef = null;

    private bool _isAutosolveClaimed = false;

    private int _stagecount = 0;

    private KugelblitzLogger _logger = null;

    public KugelblitzLobby(LobbyContentBuilder content, string id)
    {
        _content = content;
        _id = id;
        _color = KugelblitzColor.GetRandomHue(id);

        ExpectedInput = "";
    }

    public bool Subscribe(KugelblitzScript kugelblitz)
    {
        if (!_isOpen)
            return false;

        _members.Add(kugelblitz);

        UpdateVoidLinkages();

        _members.Last().AssignStage(new EmptyStage(GetLobbyBaseColor()));
        _members.Last().Highlight.material.color = GetLobbyHighlightColor().GetColor();

        KMSelectable moduleSelectable = kugelblitz.GetComponent<KMSelectable>();
        moduleSelectable.OnHighlight = () => Highlight();
        moduleSelectable.OnHighlightEnded = () => EndHighlight();

        return true;
    }

    //TP no mission only
    public bool Unsubscribe(KugelblitzScript kugelblitz)
    {
        if (!_isOpen || _content.IsMissionPreset())
            return false;

        _members.Remove(kugelblitz);

        UpdateVoidLinkages();

        KMSelectable moduleSelectable = kugelblitz.GetComponent<KMSelectable>();
        moduleSelectable.OnHighlight = () => { };
        moduleSelectable.OnHighlightEnded = () => { };

        return true;
    }

    public int GetMemberCount()
    {
        return _members.Count;
    }

    public bool IsAvailable()
    {
        return _isOpen && _content.IsAvailable(_members.Count());
    }

    public bool IsRequesting()
    {
        return _isOpen && _content.IsRequesting(_members.Count());
    }

    public int OpenSlots()
    {
        return _content.OpenSlots(_members.Count);
    }

    public void Freeze()
    {
        if (!_isOpen)
            return;

        _isOpen = false;

        _logger = new KugelblitzLogger(_members.First());

        foreach (KugelblitzScript member in _members.Skip(1))
            member.Log("Logging can be found at " + _members.First());

        _logger.WriteFormat("Using {0}preset: {1}.", _content.IsMissionPreset() ? "mission " : "", _content);
        _logger.WriteFormat("Participating modules: {0}.", _members.Join(", "));

        _startingAngle = (byte)Rnd.Range(0, 8);

        _quirks = _content.Build(_members.Count).Content;

        _quirks.Shuffle();

        for (int i = 0; i < _members.Count; i++)
        {
            _quirks[i].Generate(_members[i].RemainingSolves());

            _logger.WriteFormat("Generated {0} stages: {1}.", _members[i].RemainingSolves(), _quirks[i]);

            _stagecount += _members[i].RemainingSolves();
        }
        ExpectedInput = KugelblitzCalculation.Calculate(this);

        _members.First().PlaySound("Eerie");
    }

    public T GetQuirk<T>() where T : IKugelblitzStageManager
    {
        return (T)_quirks.First(x => x is T);
    }

    public byte GetInitialDirection()
    {
        return _startingAngle;
    }

    public void UpdateLobby()
    {
        UpdateVoidLinkages();

        UpdateSolves();
    }

    public void Highlight()
    {
        _members.ForEach(x => x.HighlightVoid());
        _linkages.ForEach(x => x.GetComponentInChildren<MeshRenderer>().enabled = true);
    }

    public void EndHighlight()
    {
        _members.ForEach(x => x.EndHighlightVoid());
        _linkages.ForEach(x => x.GetComponentInChildren<MeshRenderer>().enabled = false);
    }

    public void UpdateVoidLinkages()
    {
        while (_linkages.Count < _members.Count - 1)
        {
            _linkages.Add(_members.First().GetLinkCopy());
            _linkages.Last().SetColor(GetLobbyHighlightColor());
        }

        int linkIndex = 0;

        List<List<int>> groups = Enumerable.Range(0, _members.Count).Select(x => new List<int> { x }).ToList();

        while (groups.Count > 1)
        {
            List<int> group1 = null;
            List<int> group2 = null;
            float distance = float.PositiveInfinity;
            int index1 = 0;
            int index2 = 0;
            foreach (List<int> group in groups)
                foreach (List<int> otherGroup in groups)
                {
                    if (group == otherGroup)
                        continue;

                    foreach (int index in group)
                        foreach (int otherIndex in otherGroup)
                        {
                            float newDistance = (_members[index].Void.transform.parent.position - _members[otherIndex].Void.transform.parent.position).magnitude;
                            if (newDistance > distance * 0.99)
                                continue;

                            distance = newDistance;
                            group1 = group;
                            group2 = otherGroup;
                            index1 = index;
                            index2 = otherIndex;
                        }
                }

            _linkages[linkIndex].Transform1 = _members[index1].Void.transform.parent;
            _linkages[linkIndex].Transform2 = _members[index2].Void.transform.parent;
            linkIndex++;

            group1.AddRange(group2);
            groups.Remove(group2);
        }

        IEnumerable<VoidLinkage> discardedLinks = _linkages.Skip(linkIndex);
        _linkages = _linkages.Take(linkIndex).ToList();
        foreach (VoidLinkage linkage in discardedLinks)
        {
            GameObject.Destroy(linkage.GetComponentInChildren<MeshRenderer>());
            GameObject.Destroy(linkage);
        }
    }

    public void UpdateSolves()
    {
        //shuffle all kugelblitz quirks around that had a solve at the same time (same bomb)
        //ToArray is necessary due to the UpdateSolves condition having a side effect
        IEnumerable<KugelblitzScript> stageShiftingMods = _members.Where(x => x.UpdateSolves()).ToArray();

        if (stageShiftingMods.Count() == 0)
            return;

        if (_isOpen)
        {
            Freeze();
            for (int i = 0; i < _members.Count; i++)
            {
                _quirks[i].Return();
                _members[i].AssignStage((IKugelblitzStageViewer)_quirks[i]);
            }
            return;
        }

        _logger.WriteFormat("A module has been solved. Advancing the following modules: {0}.", stageShiftingMods.Join(", "));

        _members.First().PlaySound("Discomfort");

        IEnumerable<int> unsolvedCounts = stageShiftingMods.Select(x => x.RemainingSolves()).Distinct();

        List<KugelblitzScript> reorderedMembers = new List<KugelblitzScript>();
        List<IKugelblitzStageManager> reorderedQuirks = new List<IKugelblitzStageManager>();

        for (int i = 0; i < _members.Count; i++)
            if (!stageShiftingMods.Contains(_members[i]))
            {
                reorderedMembers.Add(_members[i]);
                reorderedQuirks.Add(_quirks[i]);
            }

        foreach (int count in unsolvedCounts)
        {
            List<KugelblitzScript> membersToProgress = new List<KugelblitzScript>();
            List<IKugelblitzStageManager> quirksToShuffle = new List<IKugelblitzStageManager>();

            for (int i = 0; i < _members.Count; i++)
                if (stageShiftingMods.Where(x => x.RemainingSolves() == count).Contains(_members[i]))
                {
                    membersToProgress.Add(_members[i]);
                    quirksToShuffle.Add(_quirks[i]);
                }

            quirksToShuffle = quirksToShuffle.Shuffle();

            if (count == 0)
            {
                if (_members.Any(x => x.RemainingSolves() > 0))
                {
                    for (int i = 0; i < membersToProgress.Count; i++)
                    {
                        membersToProgress[i].AssignStage(new EmptyStage(GetLobbyBaseColor()));
                        reorderedMembers.Add(membersToProgress[i]);
                        reorderedQuirks.Add(quirksToShuffle[i]);
                    }
                    continue;
                }

                EnterSolvableState(_content.Pacing());

                return;
            }


            for (int i = 0; i < membersToProgress.Count; i++)
            {
                reorderedMembers.Add(membersToProgress[i]);
                reorderedQuirks.Add(quirksToShuffle[i]);

                for (int j = 0; j < membersToProgress[i].StagesAdvanced(); j++)
                    quirksToShuffle[i].Advance();
                membersToProgress[i].AssignStage((IKugelblitzStageViewer)quirksToShuffle[i]);
            }
        }

        _members = reorderedMembers;
        _quirks = reorderedQuirks;
    }

    public void EnterSolvableState(float pacing)
    {
        _members.ForEach(x =>
        {
            x.EnterSolvableState(pacing);
            x.AssignStage(new ValueStage(GetLobbyBaseColor(), _startingAngle));

            x.Void.GetComponent<KMSelectable>().OnInteract = () => { x.Void.Pulse(); Insert('['); LastInputBy(x); return false; };
            x.Void.GetComponent<KMSelectable>().OnInteractEnded = () => { x.Void.Pulse(); Insert(']'); LastInputBy(x); };
        });
        _members.First().StartCoroutine(StartPulsing(pacing));
        _solvable = true;
    }

    //used to add interaction type to the sequence
    public void Insert(char action)
    {
        _input += action;
        if (_solvable && _audioRef == null && _input.StartsWith("["))
            _audioRef = _members.First().PlayRefSound("Absorbtion");
    }

    public void LastInputBy(KugelblitzScript module)
    {
        _lastInteract = module;
    }

    public void Strike()
    {
        _members.First().PlaySound("Eerie");

        _lastInteract.GetComponent<KMBombModule>().HandleStrike();
        _quirks = _quirks.Shuffle();
        _quirks.ForEach(x =>
        {
            x.CollapseStages(); x.Return();
            _logger.WriteFormat("Collapsed stages: {0}.", x);
        });

        for (int i = 0; i < _members.Count; i++)
            _members[i].AssignStage((IKugelblitzStageViewer)_quirks[i]);

        _solvable = false;
    }

    public IEnumerator Solve()
    {
        _solvable = false;
        _solved = true;

        _logger.WriteFormat("Successfully solved {0} stages of Kugelblitz. Well done!", _stagecount);
        int mat = -1;
        if (_stagecount >= 100)
            mat++;

        if (mat != -1)
        {
            _linkages.ForEach(x => x.SetMaterial(_members.First().SolveMats[mat]));
            _members.ForEach(x => x.Highlight.material = _members.First().SolveMats[mat]);
        }

        yield return null;
        _members = _members.Shuffle();
        foreach (KugelblitzScript member in _members)
        {
            _logger.WriteFormat("Solved {0}.", member);
            member.Solve();
            member.AssignStage(new EmptyStage(KugelblitzColor.None));
            _members.First().PlaySound("Decay");
            yield return new WaitForSeconds(1f);
        }
    }

    public void StopSound()
    {
        if (_audioRef == null)
            return;

        _audioRef.StopSound();
        _audioRef = null;
    }

    public void CheckInput()
    {
        if (_solvable)
        {
            if (_input.StartsWith("]"))
            {
                _input = _input.SkipWhile(x => ".]".Contains(x)).Join("");
                return;
            }

            if (!_input.EndsWith("].."))
                return;

            StopSound();

            if (_input.Replace("]..", "]") == ExpectedInput)
            {
                _members.First().StartCoroutine(Solve());
            }
            else
            {
                _logger.WriteFormat("I received {0}, but I expected {1}.", _input.Replace("]..", "]"), ExpectedInput);
                Strike();
            }



        }
        else
        {
            if (_input == "[.")
            {
                _solvable = true;
                _members.ForEach(x =>
                {
                    x.AssignStage(new ValueStage(GetLobbyBaseColor(), _startingAngle));
                });
            }
            else if (_input == "[].")
            {
                int index = _members.IndexOf(_lastInteract);
                if (_quirks[index].Advance())
                    _quirks[index].Return();
                _members[index].AssignStage((IKugelblitzStageViewer)_quirks[index]);
            }
        }

        _input = "";
    }

    private IEnumerator StartPulsing(float pacing)
    {
        yield return null;
        while (true)
        {
            yield return new WaitForSeconds(pacing);

            if (_input.Length != 0)
            {
                Insert('.');
                CheckInput();
            }

            if (_solved)
                break;

            _members.ForEach(x => x.Pulse());
            _members.First().PlaySound("Pulse");
        }
    }

    private KugelblitzColor GetLobbyBaseColor()
    {
        return new KugelblitzColor(Color.Lerp(new Color(0.25f, 0.25f, 0.25f), _color.GetColor(), 0.5f), _color.GetName());
    }

    private KugelblitzColor GetLobbyHighlightColor()
    {
        return new KugelblitzColor(Color.Lerp(new Color(1, 1, 1), _color.GetColor(), 0.25f), _color.GetName());
    }

    public string GetId()
    {
        return _id;
    }

    public bool IsAutosolveClaimed()
    {
        return _isAutosolveClaimed;
    }

    public void ClaimAutosolve()
    {
        _isAutosolveClaimed = true;
        _input = "";
    }

    public bool IsSolvable()
    {
        return _solvable;
    }

    public KugelblitzLogger GetLogger()
    {
        return _logger;
    }

    public class LobbyContent
    {
        public List<IKugelblitzStageManager> Content { get; private set; }

        // "---+??+" would mean: use green and violet and if needed pick from blue and indigo
        public LobbyContent(string properties, int count)
        {
            Content = new List<IKugelblitzStageManager> { new BaseStageManager() };

            IKugelblitzStageManager[] availableQuirks = { new OffsetStageManager(), new InvertStageManager(), new InsertStageManager(), new LengthStageManager(), new TurnStageManager(), new FlipStageManager(), new WrapStageManager() };

            Content.AddRange(Enumerable.Range(0, properties.Length).Where(x => properties[x] == '+').Select(x => availableQuirks[x])
                .ToList().Shuffle().Take(count - Content.Count));

            Content.AddRange(Enumerable.Range(0, properties.Length)
                .Where(x => properties[x] == '?')
                .ToList().Shuffle().Take(count - Content.Count)
                .Select(x => availableQuirks[x]));
        }
    }

    public class LobbyContentBuilder
    {
        private int _minSize;
        private int _maxSize;

        private string _structure;

        private float _pacing;

        private bool _isMissionPreset;

        public LobbyContentBuilder() : this(1, 8, KugelblitzScript.LastInstance.GetPreset())
        {
            _isMissionPreset = false;
        }

        public LobbyContentBuilder(int minSize, int maxSize, string structure) : this(minSize, maxSize, structure, KugelblitzScript.LastInstance.GetPacing())
        {
            
        }

        public LobbyContentBuilder(int minSize, int maxSize, string structure, float pacing)
        {
            _minSize = Math.Max(minSize, structure.Count(x => "+".Contains(x)) + 1);
            _maxSize = Math.Min(maxSize, structure.Count(x => "+?".Contains(x)) + 1);
            if (_minSize > _maxSize)
                throw new ArgumentOutOfRangeException("The lower bound should be less than or equal to the upper bound.");

            _structure = structure;

            _pacing = pacing;

            _isMissionPreset = true;
        }

        public float Pacing()
        {
            return _pacing;
        }

        public bool IsRequesting(int current)
        {
            return _minSize > current;
        }

        public bool IsAvailable(int current)
        {
            return _maxSize > current;
        }

        public int OpenSlots(int current)
        {
            return _maxSize - current;
        }

        public bool IsMissionPreset()
        {
            return _isMissionPreset;
        }

        public LobbyContent Build(int count)
        {
            return new LobbyContent(_structure, count);
        }

        public override string ToString()
        {
            return _minSize + "-" + _maxSize + ":" + _structure;
        }
    }
}

