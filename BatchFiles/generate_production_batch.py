# Batch file generator for production experiments
# Based on experiment plan from commit 1e31a00
# Generates a SINGLE batch file containing all 4 scenarios

import os

def generate_trial_block(tracking_space_file, num_users, algorithm_config):
    """Generate a single trial block"""
    lines = []
    lines.append(f"useRandomSeedSet = false")
    lines.append(f"sameRandomSeedForEachUser = false")
    lines.append(f"pathChoice = straightline")
    lines.append(f"pathLength = 200")
    lines.append(f"trackingSpaceChoice = FilePath")
    lines.append(f"trackingSpaceFilePath = {tracking_space_file}")

    # Add PredRedLPP parameters if applicable
    if 'predictionHorizon' in algorithm_config:
        lines.append(f"predictionHorizon = {algorithm_config['predictionHorizon']}")
    if 'lemniscateEndpoints' in algorithm_config:
        lines.append(f"lemniscateEndpoints = {algorithm_config['lemniscateEndpoints']}")
    if 'useMinimalActionSet' in algorithm_config:
        lines.append(f"useMinimalActionSet = {str(algorithm_config['useMinimalActionSet']).lower()}")

    # Add users
    for user_id in range(num_users):
        lines.append(f"newUser")
        lines.append(f"redirector = {algorithm_config['redirector']}")
        lines.append(f"resetter = {algorithm_config['resetter']}")

    lines.append("end")
    lines.append("")

    return "\n".join(lines)

def get_algorithms():
    """Define all 13 algorithms"""
    return [
        # 1. PredRedLPP Default (horizon=3, endpoints=11, actionSet=19)
        {
            'name': 'PredRedLPP_Default',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 3,
            'lemniscateEndpoints': 11,
            'useMinimalActionSet': False
        },
        # 2. PredRedLPP horizon=1m
        {
            'name': 'PredRedLPP_H1',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 1,
            'lemniscateEndpoints': 11,
            'useMinimalActionSet': False
        },
        # 3. PredRedLPP horizon=5m
        {
            'name': 'PredRedLPP_H5',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 5,
            'lemniscateEndpoints': 11,
            'useMinimalActionSet': False
        },
        # 4. PredRedLPP minimalActionSet=true
        {
            'name': 'PredRedLPP_MinAction',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 3,
            'lemniscateEndpoints': 11,
            'useMinimalActionSet': True
        },
        # 5. PredRedLPP endpoints=7
        {
            'name': 'PredRedLPP_E7',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 3,
            'lemniscateEndpoints': 7,
            'useMinimalActionSet': False
        },
        # 6. PredRedLPP endpoints=15
        {
            'name': 'PredRedLPP_E15',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 3,
            'lemniscateEndpoints': 15,
            'useMinimalActionSet': False
        },
        # 7. PredRedLPP Max Computation (horizon=5, endpoints=15, actionSet=19)
        {
            'name': 'PredRedLPP_MaxComp',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 5,
            'lemniscateEndpoints': 15,
            'useMinimalActionSet': False
        },
        # 8. PredRedLPP horizon=1, minimalActionSet=true
        {
            'name': 'PredRedLPP_H1_MinAction',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 1,
            'lemniscateEndpoints': 11,
            'useMinimalActionSet': True
        },
        # 9. PredRedLPP horizon=5, minimalActionSet=true
        {
            'name': 'PredRedLPP_H5_MinAction',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 5,
            'lemniscateEndpoints': 11,
            'useMinimalActionSet': True
        },
        # 10. PredRedLPP endpoints=15, minimalActionSet=true
        {
            'name': 'PredRedLPP_E15_MinAction',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 3,
            'lemniscateEndpoints': 15,
            'useMinimalActionSet': True
        },
        # 11. ThomasAPF
        {
            'name': 'ThomasAPF',
            'redirector': 'ThomasAPF',
            'resetter': 'R2G'
        },
        # 12. S2C
        {
            'name': 'S2C',
            'redirector': 'S2C',
            'resetter': 'R2G'
        },
        # 13. Null
        {
            'name': 'Null',
            'redirector': 'Null',
            'resetter': 'R2G'
        }
    ]

def generate_unified_batch_file(output_file):
    """Generate a single unified batch file containing all scenarios"""

    scenarios = [
        {
            'name': 'Scenario 1: Single User, No Obstacles',
            'tracking_space': 'TrackingSpaces/SingleSquare10m/square_10m_no_obstacle.txt',
            'num_users': 1
        },
        {
            'name': 'Scenario 2: Two Users, No Obstacles',
            'tracking_space': 'TrackingSpaces/SingleSquare10m/square_10m_no_obstacle_2users.txt',
            'num_users': 2
        },
        {
            'name': 'Scenario 3: Single User, 4 Obstacles',
            'tracking_space': 'TrackingSpaces/SingleSquare10m/square_10m_four_obstacles.txt',
            'num_users': 1
        },
        {
            'name': 'Scenario 4: Two Users, 4 Obstacles',
            'tracking_space': 'TrackingSpaces/SingleSquare10m/square_10m_four_obstacles_2users.txt',
            'num_users': 2
        }
    ]

    algorithms = get_algorithms()
    trials_per_algorithm = 30
    total_trials = len(scenarios) * len(algorithms) * trials_per_algorithm

    content = []
    content.append("// ====================================================================")
    content.append("// Production Experiment Batch File")
    content.append("// Based on experiment plan from commit 1e31a00")
    content.append("// Path: StraightLine")
    content.append("// ====================================================================")
    content.append("//")
    content.append("// Structure:")
    content.append(f"//   4 scenarios x 13 algorithms x {trials_per_algorithm} trials = {total_trials} total trials")
    content.append("//")
    content.append("// Scenarios:")
    for i, s in enumerate(scenarios, 1):
        content.append(f"//   {i}. {s['name']}")
    content.append("//")
    content.append("// Algorithms (13 total):")
    for i, alg in enumerate(algorithms, 1):
        content.append(f"//   {i}. {alg['name']}")
    content.append("//")
    content.append("// ====================================================================")
    content.append("")

    for scenario in scenarios:
        content.append("// ====================================================================")
        content.append(f"// {scenario['name']}")
        content.append(f"// Tracking Space: {scenario['tracking_space']}")
        content.append(f"// Users: {scenario['num_users']}")
        content.append("// ====================================================================")
        content.append("")

        for alg in algorithms:
            content.append(f"// --- {alg['name']} ({trials_per_algorithm} trials) ---")

            for trial in range(trials_per_algorithm):
                content.append(generate_trial_block(
                    scenario['tracking_space'],
                    scenario['num_users'],
                    alg
                ))

    with open(output_file, 'w', encoding='utf-8') as f:
        f.write("\n".join(content))

    print(f"Generated unified batch file: {output_file}")
    print(f"Total trials: {total_trials}")
    print(f"  - {len(scenarios)} scenarios")
    print(f"  - {len(algorithms)} algorithms")
    print(f"  - {trials_per_algorithm} trials per algorithm")

def main():
    batch_dir = os.path.dirname(os.path.abspath(__file__))
    output_path = os.path.join(batch_dir, 'production_experiment_all.txt')

    print("Generating unified production batch file...")
    print("=" * 60)

    generate_unified_batch_file(output_path)

    print("=" * 60)
    print("Done!")

if __name__ == "__main__":
    main()
