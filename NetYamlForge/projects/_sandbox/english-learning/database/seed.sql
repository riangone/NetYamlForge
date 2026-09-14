-- English Vocabulary Seed Data
INSERT OR IGNORE INTO EnglishWord (Word, Phonetic, Meaning, Difficulty, ExampleSentence, ExampleTranslation) VALUES
('apple', '[ˈæpl]', '苹果', 1, 'She ate a ripe red apple.', '她吃了一个成熟的红苹果。'),
('banana', '[bəˈnænə]', '香蕉', 1, 'Bananas are rich in potassium.', '香蕉富含钾。'),
('cat', '[kæt]', '猫', 1, 'The cat is sleeping on the sofa.', '猫正在沙发上睡觉。'),
('dog', '[dɒɡ]', '狗', 1, 'A friendly dog wagged its tail.', '一只友善的狗摇了摇尾巴。'),
('house', '[haʊs]', '房子', 1, 'They bought a beautiful house near the lake.', '他们在湖边买了一栋漂亮的房子。'),
('challenge', '[ˈtʃæləndʒ]', '挑战', 2, 'Climbing this mountain is a huge challenge.', '爬这座山是一个巨大的挑战。'),
('victory', '[ˈvɪktəri]', '胜利', 2, 'The team celebrated their historic victory.', '队员们庆祝了他们历史性的胜利。'),
('mountain', '[ˈmaʊntɪn]', '山；山脉', 2, 'They climbed the mountain at dawn.', '他们在黎明时分登上了山顶。'),
('ocean', '[ˈoʊʃən]', '海洋', 2, 'The ocean is home to millions of species.', '海洋是数百万物种的家园。'),
('forest', '[ˈfɔːrɪst]', '森林', 2, 'The forest was full of birdsong.', '森林里鸟鸣声不断。'),
('accomplish', '[əˈkʌmplɪʃ]', '完成；实现', 3, 'You can accomplish anything with hard work.', '只要努力，你可以实现任何事情。'),
('diligence', '[ˈdɪlɪdʒəns]', '勤奋', 3, 'Diligence is the mother of success.', '勤奋是成功之母。'),
('cooperation', '[koʊˌɒpəˈreɪʃn]', '合作', 3, 'Close cooperation is needed to finish the project.', '完成该项目需要紧密合作。'),
('support', '[səˈpɔːt]', '支持', 3, 'Thank you for your warm support.', '谢谢你们的热情支持。'),
('focus', '[ˈfoʊkəs]', '焦点；聚焦', 3, 'We must focus on key priorities.', '我们必须集中精力于关键的优先事项。'),
('benevolent', '[bəˈnevələnt]', '仁慈的；好心肠的', 4, 'The benevolent donor built a new school for the village.', '这位仁慈的捐赠者为村庄建了一所新学校。'),
('resilient', '[rɪˈzɪliənt]', '有韧性的；恢复力强的', 4, 'Children are often remarkably resilient.', '孩子们往往有极强的恢复力。'),
('eloquent', '[ˈeləkwənt]', '雄辩的；有说服力的', 4, 'He made an eloquent speech at the wedding.', '他在婚礼上发表了极其精彩动人的演讲。'),
('ambiguous', '[æmˈbɪɡjuəs]', '模棱两可的；含糊的', 4, 'The contract included some ambiguous terms.', '合同里包含了一些模棱两可的条款。'),
('tenacious', '[tɪˈneɪʃəs]', '坚韧的；顽强的', 4, 'He is a tenacious negotiator.', '他是一位顽强的谈判者。'),
('ephemeral', '[ɪˈfemərəl]', '短暂的；瞬息即逝的', 5, 'Fame in the internet age is often ephemeral.', '互联网时代的名声往往是短暂的。'),
('ubiquitous', '[juːˈbɪkwɪtəs]', '无处不在的', 5, 'Smartphones have become ubiquitous in daily life.', '智能手机在日常生活中已变得无处不在。'),
('zenith', '[ˈzenɪθ]', '顶点；最高点', 5, 'At the zenith of his career, he decided to retire.', '在他事业的巅峰时期，他决定退休。'),
('sycophant', '[ˈsɪkəfænt]', '谄媚者；马屁精', 5, 'The king was surrounded by sycophants.', '国王身边尽是阿谀奉承之人。'),
('perspicacious', '[ˌpɜːspɪˈkeɪʃəs]', '有洞察力的；明察秋毫的', 5, 'A perspicacious reader will notice the hidden irony.', '有洞察力的读者会发现隐藏的讽刺意图。');

-- English Exercises Seed Data
INSERT OR IGNORE INTO EnglishExercise (Type, Question, Choices, Answer, Difficulty) VALUES
('choice_word_to_meaning', 'What is the meaning of the word "apple"?', '["苹果", "香蕉", "西瓜", "梨"]', '苹果', 1),
('choice_meaning_to_word', 'Which word means "房子"?', '["apple", "banana", "house", "dog"]', 'house', 1),
('fill_blank', 'The cat is sleeping ___ the sofa.', '["on", "at", "in", "by"]', 'on', 1),
('translate', 'Translate: "他吃了一个成熟的红苹果。"', NULL, 'She ate a ripe red apple.', 1),
('choice_word_to_meaning', 'What is the meaning of the word "challenge"?', '["胜利", "挑战", "山脉", "森林"]', '挑战', 2),
('choice_meaning_to_word', 'Which word means "胜利"?', '["challenge", "victory", "mountain", "ocean"]', 'victory', 2),
('fill_blank', 'They climbed the mountain ___ dawn.', '["at", "on", "in", "by"]', 'at', 2),
('translate', 'Translate: "爬这座山是一个巨大的挑战。"', NULL, 'Climbing this mountain is a huge challenge.', 2);

-- Default Learning Profiles for admin and initial test setup
INSERT OR IGNORE INTO LearningProfile (UserName, CurrentLevel, TotalXp, Streak, LastActiveDate, CompletedLessonsCount) VALUES
('admin', 1, 150, 3, '2026-08-18', 4);

-- Daily Lesson for admin for today
INSERT OR IGNORE INTO DailyLesson (UserName, LessonDate, TargetLevel, WordIds, ExerciseIds, Status, Score) VALUES
('admin', '2026-08-19', 1, '1,2,3,4,5', '1,2,3,4', 'pending', 0);

-- Past Learning History for admin
INSERT OR IGNORE INTO LearningHistory (UserName, LessonId, CompletedAt, TotalQuestions, CorrectAnswers, XpEarned) VALUES
('admin', 1, '2026-08-18 10:30:00', 4, 4, 50),
('admin', 1, '2026-08-17 09:15:00', 4, 3, 40),
('admin', 1, '2026-08-16 15:45:00', 4, 4, 50),
('admin', 1, '2026-08-15 11:00:00', 4, 2, 30);
