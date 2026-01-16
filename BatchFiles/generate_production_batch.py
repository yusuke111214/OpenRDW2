# Batch file generator for production experiments
# Based on experiment plan from commit 1e31a00

import os

def generate_trial_block(tracking_space_file, num_users, algorithm_config, trial_num=1):
    """Generate a single trial block"""
    lines = []
    lines.append(f"useRandomSeedSet = false")
    lines.append(f"sameRandomSeedForEachUser = false")
    lines.append(f"pathChoice = randomturn")
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

def generate_batch_file(scenario_name, tracking_space_file, num_users, output_file):
    """Generate complete batch file for one scenario"""

    # Define all 13 algorithms
    algorithms = [
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
        # 8. PredRedLPP horizon=1, minimalActionSet=true (additional variation)
        {
            'name': 'PredRedLPP_H1_MinAction',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 1,
            'lemniscateEndpoints': 11,
            'useMinimalActionSet': True
        },
        # 9. PredRedLPP horizon=5, minimalActionSet=true (additional variation)
        {
            'name': 'PredRedLPP_H5_MinAction',
            'redirector': 'PredRedLPP',
            'resetter': 'R2G',
            'predictionHorizon': 5,
            'lemniscateEndpoints': 11,
            'useMinimalActionSet': True
        },
        # 10. PredRedLPP endpoints=15, minimalActionSet=true (additional variation)
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

    trials_per_algorithm = 30

    content = []
    content.append(f"// Production Experiment Batch File - {scenario_name}")
    content.append(f"// Based on experiment plan from commit 1e31a00")
    content.append(f"// Tracking space: {tracking_space_file}")
    content.append(f"// Users: {num_users}")
    content.append(f"// Total trials: {len(algorithms)} algorithms x {trials_per_algorithm} trials = {len(algorithms) * trials_per_algorithm}")
    content.append(f"//")
    content.append(f"// Algorithms:")
    for i, alg in enumerate(algorithms, 1):
        content.append(f"// {i}. {alg['name']}")
    content.append("")

    for alg in algorithms:
        content.append(f"// ====================================================================")
        content.append(f"// {alg['name']} - {trials_per_algorithm} trials")
        content.append(f"// ====================================================================")
        content.append("")

        for trial in range(trials_per_algorithm):
            content.append(generate_trial_block(tracking_space_file, num_users, alg, trial + 1))

    with open(output_file, 'w', encoding='utf-8') as f:
        f.write("\n".join(content))

    print(f"Generated: {output_file}")
    print(f"  - {len(algorithms)} algorithms x {trials_per_algorithm} trials = {len(algorithms) * trials_per_algorithm} trials")

def main():
    batch_dir = os.path.dirname(os.path.abspath(__file__))

    scenarios = [
        {
            'name': 'SingleUser_NoObstacle',
            'tracking_space': 'TrackingSpaces/SingleSquare10m/square_10m_no_obstacle.txt',
            'num_users': 1,
            'output': 'production_1user_no_obstacle.txt'
        },
        {
            'name': 'TwoUsers_NoObstacle',
            'tracking_space': 'TrackingSpaces/SingleSquare10m/square_10m_no_obstacle_2users.txt',
            'num_users': 2,
            'output': 'production_2users_no_obstacle.txt'
        },
        {
            'name': 'SingleUser_4Obstacles',
            'tracking_space': 'TrackingSpaces/SingleSquare10m/square_10m_four_obstacles.txt',
            'num_users': 1,
            'output': 'production_1user_4obstacles.txt'
        },
        {
            'name': 'TwoUsers_4Obstacles',
            'tracking_space': 'TrackingSpaces/SingleSquare10m/square_10m_four_obstacles_2users.txt',
            'num_users': 2,
            'output': 'production_2users_4obstacles.txt'
        }
    ]

    print("Generating production batch files...")
    print("=" * 60)

    for scenario in scenarios:
        output_path = os.path.join(batch_dir, scenario['output'])
        generate_batch_file(
            scenario['name'],
            scenario['tracking_space'],
            scenario['num_users'],
            output_path
        )

    print("=" * 60)
    print("All batch files generated successfully!")
    print("\nTotal experiments:")
    print(f"  4 scenarios x 13 algorithms x 30 trials = 1560 trials")

if __name__ == "__main__":
    main()
