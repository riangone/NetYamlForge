CREATE TABLE IF NOT EXISTS high_scores (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_name TEXT NOT NULL,
    score INTEGER NOT NULL DEFAULT 0,
    correct INTEGER DEFAULT 0,
    incorrect INTEGER DEFAULT 0,
    max_combo INTEGER DEFAULT 0,
    difficulty INTEGER DEFAULT 1,
    played_at TEXT
);

CREATE TABLE IF NOT EXISTS vocab_words (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    word TEXT NOT NULL UNIQUE,
    phonetic TEXT,
    meaning TEXT NOT NULL,
    difficulty INTEGER DEFAULT 1,
    example TEXT,
    example_meaning TEXT
);

CREATE TABLE IF NOT EXISTS learning_records (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    word_id INTEGER NOT NULL,
    user_name TEXT NOT NULL,
    status TEXT DEFAULT 'learning',
    test_count INTEGER DEFAULT 0,
    correct_count INTEGER DEFAULT 0,
    last_tested_at TEXT,
    FOREIGN KEY(word_id) REFERENCES vocab_words(id),
    UNIQUE(word_id, user_name)
);

INSERT OR IGNORE INTO vocab_words (word, phonetic, meaning, difficulty, example, example_meaning) VALUES
('apple', '[ˈæpl]', '苹果', 1, 'She ate a ripe red apple.', '她吃了一个成熟的红苹果。'),
('banana', '[bəˈnænə]', '香蕉', 1, 'Bananas are rich in potassium.', '香蕉富含钾。'),
('challenge', '[ˈtʃæləndʒ]', '挑战', 2, 'Climbing this mountain is a huge challenge.', '爬这座山是一个巨大的挑战。'),
('victory', '[ˈvɪktəri]', '胜利', 2, 'The team celebrated their historic victory.', '队员们庆祝了他们历史性的胜利。'),
('accomplish', '[əˈkʌmplɪʃ]', '完成；实现', 3, 'You can accomplish anything with hard work.', '只要努力，你可以实现任何事情。'),
('benevolent', '[bəˈnevələnt]', '仁慈的；好心肠的', 4, 'The benevolent donor built a new school for the village.', '这位仁慈的捐赠者为村庄建了一所新学校。'),
('ephemeral', '[ɪˈfemərəl]', '短暂的；瞬息即逝的', 5, 'Fame in the internet age is often ephemeral.', '互联网时代的名声往往是短暂的。'),
('magnificent', '[mæɡˈnɪfɪsnt]', '壮丽的；宏伟的', 3, 'The palace provides a magnificent view of the city.', '从这座宫殿可以饱览城市壮丽的景色。'),
('adventure', '[ədˈventʃə(r)]', '冒险；奇遇', 2, 'They went on an exciting forest adventure.', '他们进行了一次刺激的森林冒险。'),
('curiosity', '[ˌkjʊəriˈɒsəti]', '好奇心', 2, 'Curiosity killed the cat.', '好奇心害死猫。'),
('diligence', '[ˈdɪlɪdʒəns]', '勤奋', 3, 'Diligence is the mother of success.', '勤奋是成功之母。'),
('eloquent', '[ˈeləkwənt]', '雄辩的；有说服力的', 4, 'He made an eloquent speech at the wedding.', '他在婚礼上发表了极其精彩动人的演讲。'),
('resilient', '[rɪˈzɪliənt]', '有韧性的；恢复力强的', 4, 'Children are often remarkably resilient.', '孩子们往往有极强的恢复力。'),
('ubiquitous', '[juːˈbɪkwɪtəs]', '无处不在的', 5, 'Smartphones have become ubiquitous in daily life.', '智能手机在日常生活中已变得无处不在。'),
('nostalgia', '[nɒˈstældʒə]', '怀旧；乡愁', 3, 'The old photos filled her with nostalgia.', '老照片让她心中充满了怀旧之情。'),
('meticulous', '[məˈtɪkjələs]', '一丝不苟的；非常细心的', 4, 'He is meticulous about his personal appearance.', '他对自己的外表非常注重，一丝不苟。'),
('gorgeous', '[ˈɡɔːdʒəs]', '极其漂亮的；绚丽的', 2, 'The sunset over the beach was gorgeous.', '海滩上的落日太美了。'),
('prosperous', '[ˈprɒspərəs]', '繁荣的；兴旺的', 3, 'They wish you a happy and prosperous new year.', '他们祝你新年快乐，万事如意。'),
('zenith', '[ˈzenɪθ]', '顶点；最高点', 5, 'At the zenith of his career, he decided to retire.', '在他事业的巅峰时期，他决定退休。'),
('abundance', '[əˈbʌndəns]', '丰富；充足', 3, 'The area has an abundance of natural resources.', '这个地区有丰富的自然资源。'),
-- Difficulty 1: common everyday words
('ocean', '[ˈoʊʃən]', '海洋', 1, 'The ocean is home to millions of species.', '海洋是数百万物种的家园。'),
('forest', '[ˈfɔːrɪst]', '森林', 1, 'The forest was full of birdsong.', '森林里鸟鸣声不断。'),
('mountain', '[ˈmaʊntɪn]', '山；山脉', 1, 'They climbed the mountain at dawn.', '他们在黎明时分登上了山顶。'),
('sunshine', '[ˈsʌnʃaɪn]', '阳光；晴天', 1, 'The children played in the sunshine.', '孩子们在阳光下玩耍。'),
('library', '[ˈlaɪbrəri]', '图书馆', 1, 'She spent the afternoon at the library.', '她在图书馆度过了下午。'),
('friendship', '[ˈfrendʃɪp]', '友谊', 1, 'Their friendship has lasted for decades.', '他们的友谊延续了数十年。'),
('garden', '[ˈɡɑːrdən]', '花园', 1, 'He grows vegetables in his garden.', '他在花园里种蔬菜。'),
-- Difficulty 2: intermediate common words
('breathtaking', '[ˈbreθteɪkɪŋ]', '令人叹为观止的', 2, 'The view from the cliff was breathtaking.', '悬崖上的景色令人叹为观止。'),
('motivation', '[ˌmoʊtɪˈveɪʃən]', '动力；动机', 2, 'Money alone is not enough motivation.', '单靠金钱并不足以成为动力。'),
('obstacle', '[ˈɒbstəkl]', '障碍；阻碍', 2, 'Fear is the biggest obstacle to success.', '恐惧是成功最大的障碍。'),
('consequence', '[ˈkɒnsɪkwəns]', '后果；结果', 2, 'Think about the consequences of your actions.', '想清楚你的行为将带来的后果。'),
('atmosphere', '[ˈætməsfɪə]', '气氛；大气层', 2, 'The restaurant had a warm, cosy atmosphere.', '这家餐厅有着温馨舒适的氛围。'),
('accomplish', '[əˈkʌmplɪʃ]', '完成；实现', 2, 'She accomplished her goal in record time.', '她以创纪录的速度实现了目标。'),
('generous', '[ˈdʒenərəs]', '慷慨的', 2, 'It was generous of you to help a stranger.', '你愿意帮助陌生人，真是慷慨。'),
-- Difficulty 3: upper-intermediate
('perseverance', '[ˌpɜːrsɪˈvɪərəns]', '坚持不懈；毅力', 3, 'Perseverance is the key to mastering any skill.', '坚持不懈是掌握任何技能的关键。'),
('inevitable', '[ɪnˈevɪtəbl]', '不可避免的', 3, 'Change is inevitable in a growing company.', '在成长中的公司里，变化是不可避免的。'),
('ambiguous', '[æmˈbɪɡjuəs]', '模棱两可的；含糊的', 3, 'The contract included some ambiguous terms.', '合同里包含了一些模棱两可的条款。'),
('reconcile', '[ˈrekənsaɪl]', '和解；调和', 3, 'It took years for them to reconcile.', '他们花了多年时间才和解。'),
('substantial', '[səbˈstænʃəl]', '大量的；实质性的', 3, 'The project required a substantial investment.', '这个项目需要大量投资。'),
('vivid', '[ˈvɪvɪd]', '生动的；鲜明的', 3, 'She has a vivid imagination.', '她有着生动的想象力。'),
('compromise', '[ˈkɒmprəmaɪz]', '妥协；折中方案', 3, 'Both sides reached a compromise after hours of talks.', '双方经过数小时的谈判达成了妥协。'),
-- Difficulty 4: advanced
('ambivalent', '[æmˈbɪvələnt]', '矛盾的；犹豫不决的', 4, 'She felt ambivalent about leaving her hometown.', '她对离开家乡感到矛盾。'),
('tenacious', '[tɪˈneɪʃəs]', '坚韧的；顽强的', 4, 'He is a tenacious negotiator.', '他是一位顽强的谈判者。'),
('precipitate', '[prɪˈsɪpɪteɪt]', '促使；仓促的', 4, 'The scandal precipitated his resignation.', '这场丑闻促使他辞职。'),
('candid', '[ˈkændɪd]', '坦诚的；坦率的', 4, 'I appreciate your candid feedback.', '我很欣赏你坦率的反馈。'),
('astute', '[əˈstjuːt]', '精明的；睿智的', 4, 'She made an astute business decision.', '她做出了一个精明的商业决策。'),
('pragmatic', '[præɡˈmætɪk]', '务实的；实用主义的', 4, 'A pragmatic approach is often more effective.', '务实的方法往往更有效。'),
('exacerbate', '[ɪɡˈzæsərbeɪt]', '加剧；使恶化', 4, 'Stress can exacerbate health problems.', '压力会加剧健康问题。'),
-- Difficulty 5: academic / literary
('sycophant', '[ˈsɪkəfænt]', '谄媚者；马屁精', 5, 'The king was surrounded by sycophants.', '国王身边尽是阿谀奉承之人。'),
('perfidious', '[pəˈfɪdiəs]', '背信弃义的', 5, 'The perfidious ally betrayed them at the last moment.', '那个背信弃义的盟友在最后关头背叛了他们。'),
('loquacious', '[ləˈkweɪʃəs]', '多话的；爱说话的', 5, 'His loquacious nature made meetings run long.', '他话多的天性让会议总是超时。'),
('recalcitrant', '[rɪˈkælsɪtrənt]', '顽固不化的；不服从的', 5, 'The recalcitrant student refused to follow any rules.', '那个顽固的学生拒绝遵守任何规定。'),
('obfuscate', '[ˈɒbfʌskeɪt]', '使模糊；使困惑', 5, 'Jargon is often used to obfuscate simple ideas.', '行话经常被用来掩盖简单的概念。'),
('solipsism', '[ˈsɒlɪpsɪzəm]', '唯我论；极端自我主义', 5, 'His solipsism made it impossible to collaborate.', '他的极端自我主义使协作成为不可能。'),
('perspicacious', '[ˌpɜːspɪˈkeɪʃəs]', '有洞察力的；明察秋毫的', 5, 'A perspicacious reader will notice the hidden irony.', '有洞察力的读者会发现隐藏的讽刺意味。');
