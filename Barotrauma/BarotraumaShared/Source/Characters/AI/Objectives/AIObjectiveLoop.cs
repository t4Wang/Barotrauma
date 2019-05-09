﻿using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    abstract class AIObjectiveLoop<T> : AIObjective
    {
        protected HashSet<T> targets = new HashSet<T>();
        protected Dictionary<T, AIObjective> objectives = new Dictionary<T, AIObjective>();
        protected HashSet<T> ignoreList = new HashSet<T>();
        private float ignoreListTimer;
        private float targetUpdateTimer;

        // By default, doesn't clear the list automatically
        protected virtual float IgnoreListClearInterval => 0;

        public HashSet<T> ReportedTargets { get; private set; } = new HashSet<T>();

        public bool AddTarget(T target)
        {
            if (ReportedTargets.Contains(target))
            {
                return false;
            }
            if (Filter(target))
            {
                ReportedTargets.Add(target);
                return true;
            }
            return false;
        }

        public AIObjectiveLoop(Character character, AIObjectiveManager objectiveManager, float priorityModifier, string option = null) 
            : base(character, objectiveManager, priorityModifier, option)
        {
            Reset();
        }

        protected override void Act(float deltaTime) { }
        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override bool IsLoop { get => true; set => throw new System.Exception("Trying to set the value for IsLoop from: " + System.Environment.StackTrace); }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (IgnoreListClearInterval > 0)
            {
                if (ignoreListTimer > IgnoreListClearInterval)
                {
                    Reset();
                }
                else
                {
                    ignoreListTimer += deltaTime;
                }
            }
            if (targetUpdateTimer < 0)
            {
                UpdateTargets();
            }
            else
            {
                targetUpdateTimer -= deltaTime;
            }
            // Sync objectives, subobjectives and targets
            foreach (var objective in objectives)
            {
                var target = objective.Key;
                if (!objective.Value.CanBeCompleted)
                {
                    ignoreList.Add(target);
                    targetUpdateTimer = 0;
                }
                if (!targets.Contains(target))
                {
                    subObjectives.Remove(objective.Value);
                }
            }
            SyncRemovedObjectives(objectives, GetList());
            if (objectives.None() && targets.Any())
            {
                CreateObjectives();
            }
        }

        // the timer is set between 1 and 10 seconds, depending on the priority
        private float SetTargetUpdateTimer() => targetUpdateTimer = 1 / MathHelper.Clamp(PriorityModifier * Rand.Range(0.75f, 1.25f), 0.1f, 1);

        public override void Reset()
        {
            ignoreList.Clear();
            ignoreListTimer = 0;
            UpdateTargets();
        }

        public override void OnSelected()
        {
            base.OnSelected();
            if (HumanAIController.ObjectiveManager.CurrentOrder == this)
            {
                Reset();
            }
        }

        public override float GetPriority()
        {
            if (character.Submarine == null) { return 0; }
            if (targets.None()) { return 0; }
            // Allow the target value to be more than 100.
            float targetValue = TargetEvaluation();
            // If the target value is less than 1% of the max value, let's just treat it as zero.
            if (targetValue < 1) { return 0; }
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            float max = MathHelper.Min(AIObjectiveManager.OrderPriority - 1, 90);
            float devotion = MathHelper.Min(10, Priority);
            float value = MathHelper.Clamp((devotion + targetValue * PriorityModifier) / 100, 0, 1);
            return MathHelper.Lerp(0, max, value);
        }

        protected void UpdateTargets()
        {
            SetTargetUpdateTimer();
            targets.Clear();
            FindTargets();
            CreateObjectives();
        }

        protected virtual void FindTargets()
        {
            foreach (T target in GetList())
            {
                // The bots always find targets when the objective is an order.
                if (objectiveManager.CurrentOrder != this)
                {
                    // Battery or pump states cannot currently be reported (not implemented) and therefore we must ignore them -> the bots always know if they require attention.
                    bool ignore = this is AIObjectiveChargeBatteries || this is AIObjectivePumpWater;
                    if (!ignore && !ReportedTargets.Contains(target)) { continue; }
                }
                if (!Filter(target)) { continue; }
                if (!ignoreList.Contains(target))
                {
                    targets.Add(target);
                }
            }
        }

        protected virtual void CreateObjectives()
        {
            foreach (T target in targets)
            {
                if (!objectives.TryGetValue(target, out AIObjective objective))
                {
                    objective = ObjectiveConstructor(target);
                    objective.Completed += () => ReportedTargets.Remove(target);
                    objectives.Add(target, objective);
                    AddSubObjective(objective);
                }
            }
        }

        /// <summary>
        /// List of all possible items of the specified type. Used for filtering the removed objectives.
        /// </summary>
        protected abstract IEnumerable<T> GetList();

        protected abstract float TargetEvaluation();

        protected abstract AIObjective ObjectiveConstructor(T target);
        protected abstract bool Filter(T target);
    }
}
