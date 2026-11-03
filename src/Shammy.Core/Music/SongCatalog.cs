namespace Shammy.Core.Music;

/// <summary>
/// Popular Hindi/Indian songs and artists, romanized the way streaming services spell them. Whisper has
/// rarely seen these words written down, so it hears "play kesariya" as "Play case Rhea"; listing them in
/// the prompt of a second pass, and matching what it heard against them, fixes the spelling.
/// Roughly most popular first: when the prompt budget runs out, the end of each list is what's left out.
/// </summary>
public static class SongCatalog
{
    public static IReadOnlyList<string> Songs { get; } = new[]
    {
        "Kesariya", "Tum Hi Ho", "Channa Mereya", "Apna Bana Le", "Raataan Lambiyan", "Pasoori", "Tere Vaaste",
        "Heeriye", "Satranga", "Chaleya", "Jhoome Jo Pathaan", "Besharam Rang", "Tum Kya Mile", "O Maahi", "Lutt Putt Gaya",
        "Phir Aur Kya Chahiye", "Maan Meri Jaan", "Kahani Suno", "Tujhe Kitna Chahne Lage", "Kabira", "Agar Tum Saath Ho",
        "Ilahi", "Bekhayali", "Tera Ban Jaunga", "Shayad", "Hawayein", "Galliyan", "Phir Bhi Tumko Chaahunga",
        "Ae Dil Hai Mushkil", "Raabta", "Janam Janam", "Gerua", "Kalank", "Pehli Nazar Mein", "Tujh Mein Rab Dikhta Hai",
        "Tum Se Hi", "Kun Faya Kun", "Agar Tum Mil Jao", "Tera Hone Laga Hoon", "Pee Loon", "Saibo", "Muskurane",
        "Sanam Re", "Baarish", "Tum Jo Aaye", "Main Rang Sharbaton Ka", "Khairiyat", "Dil Diyan Gallan", "Hasi",
        "Duniyaa", "Thodi Jagah", "Pal", "Ve Maahi", "Mann Bharryaa", "Lut Gaye", "Dil Galti Kar Baitha Hai",
        "Barsaat Ki Dhun", "Tera Yaar Hoon Main", "Bulleya", "Jag Ghoomeya", "Zaalima", "Nashe Si Chadh Gayi",
        "Kaun Tujhe", "Jab Koi Baat", "Hamari Adhuri Kahani", "Main Tenu Samjhawan Ki", "Tera Chehra", "Teri Mitti",
        "Ae Watan", "Maa Tujhe Salaam", "Vande Mataram", "Chak De India", "Kar Har Maidaan Fateh", "Zinda",
        "Kudi Nu Nachne De", "Kala Chashma", "Kar Gayi Chull", "Badtameez Dil", "Balam Pichkari", "London Thumakda",
        "Gallan Goodiyaan", "Nachde Ne Saare", "Kamariya", "Dilbar", "Garmi", "Saki Saki", "Aankh Marey", "Coca Cola",
        "Lamberghini", "High Rated Gabru", "Lahore", "Naah", "Taaron Ke Shehar", "Brown Munde", "Excuses", "Softly",
        "Elevated", "Insane", "Same Beef", "So High", "295", "Levels", "Lover", "Daku", "Kya Baat Ay", "Titliaan",
        "Filhall", "Bijlee Bijlee", "Mere Mehboob Mere Sanam", "Manike", "Dil Ko Karaar Aaya", "Baarishein", "Ranjha",
        "Raatan Lambiyan", "Lehra Do", "Mann Mera", "Jeena Jeena", "Saware", "Tu Hai Kahan", "Tu Jaane Na", "Iktara",
        "Kaise Hua", "Tujhe Sochta Hoon", "Hosanna", "Kya Mujhe Pyaar Hai", "Pehla Nasha", "Tujhe Dekha To", "Chaiyya Chaiyya",
        "Kal Ho Naa Ho", "Kuch Kuch Hota Hai", "Dil To Pagal Hai", "Suraj Hua Maddham", "Tum Paas Aaye", "Mitwa", "Maahi Ve",
        "Kajra Re", "Sheila Ki Jawani", "Munni Badnaam Hui", "Chammak Challo", "Desi Girl", "Dhoom Machale", "Nagada Sang Dhol",
        "Malhari", "Deewani Mastani", "Pinga", "Ghoomar", "Ek Do Teen", "Dola Re Dola", "Mauja Hi Mauja", "Senorita",
        "Ainvayi Ainvayi", "Gal Mitthi Mitthi", "Ude Dil Befikre", "Matargashti", "Yeh Ishq Hai",
        "Jeene Laga Hoon", "Tum Mile", "Abhi Mujh Mein Kahin", "Ishq Wala Love", "Radha", "Mast Magan",
        "Manwa Laage", "Sooraj Dooba Hai", "Tu Meri", "Chal Wahan Jaate Hain", "Yaariyan", "Enna Sona", "Kho Gaye Hum Kahan",
        "Banjaara", "Tere Sang Yaara", "Sanu Ek Pal Chain", "O Saathi", "Dil Meri Na Sune", "Ghungroo", "Bom Diggy",
        "Proper Patola", "Ishare Tere", "Tera Ghata", "Mere Naam Tu", "Tere Naal", "Ik Vaari Aa", "Hawa Banke",
        "Woh Din", "Rait Zara Si", "Aaj Phir", "Hua Hai Aaj Pehli Baar", "Haan Main Galat", "Shiv Kailash",
        "Namo Namo", "Hanuman Chalisa", "Ram Siya Ram", "Shree Ram Janki", "Achyutam Keshavam", "Om Jai Jagdish Hare",
        "Lag Ja Gale", "Tujhse Naraz Nahi Zindagi", "Ek Pyar Ka Nagma Hai", "Pal Pal Dil Ke Paas", "Mere Sapno Ki Rani",
        "Chura Liya Hai Tumne", "Kabhi Kabhi Mere Dil Mein", "Tere Bina Zindagi Se", "Yeh Shaam Mastani", "Gulabi Aankhen",
        "Ajeeb Dastan Hai Yeh", "Aaoge Jab Tum", "Tadap Tadap", "Aankhon Mein Teri", "Tu Hi Meri Shab Hai", "Ya Ali",
        "Jiya Dhadak Dhadak", "Aadat", "Woh Lamhe", "Bheegi Bheegi", "Tera Mera Rishta", "Tu Jo Mila", "Saiyaara",
        "Chuttamalle", "Naatu Naatu", "Srivalli", "Oo Antava", "Saami Saami", "Kurchi Madathapetti", "Arabic Kuthu",
        "Vaathi Coming", "Rowdy Baby", "Why This Kolaveri Di", "Kannazhaga", "Hukum", "Kaavaalaa", "Naa Ready",
        "Butta Bomma", "Samajavaragamana", "Inkem Inkem", "Kalaavathi", "Ranjithame", "Jimikki Kammal",
        "Tauba Tauba", "Aaj Ki Raat", "Sajni", "Akhiyaan Gulaab", "Pehle Bhi Main", "Arjan Vailly", "Hua Main",
        "Ishq Hai", "Khalasi", "Kahani Suno 2.0", "Jo Tum Mere Ho", "Husn", "Baarishon Mein", "Choo Lo", "Tera Mera Pyar",
        "Mast Malang Jhoom", "Ve Kamleya", "What Jhumka", "Dhindhora Baje Re", "Zihaal E Miskin",
        "Kya Loge Tum", "Mere Yaaraa", "Mere Dholna", "Ami Je Tomar", "Afreen Afreen", "Mere Rashke Qamar", "Tere Bin",
        "Dil Dil Pakistan", "Kana Yaari", "Tu Jhoom", "Peechay Hutt", "Aankhon Se Batana", "Jhol", "Gul", "Faasle",
        "Sang Rahiyo", "Tum Prem Ho", "Ishq Sufiyana", "Hasnu Muskaanu", "Mehrama", "Teri Deewani", "Saiyyan",
        "Allah Ke Bande", "Sayaani", "Rabba Mehar Kari", "Main Agar Kahoon", "Ajab Si", "Dard E Disco", "Om Shanti Om",
    };

    public static IReadOnlyList<string> Artists { get; } = new[]
    {
        "Arijit Singh", "Shreya Ghoshal", "Atif Aslam", "Jubin Nautiyal", "Pritam", "A R Rahman", "Sonu Nigam",
        "Neha Kakkar", "Badshah", "Diljit Dosanjh", "AP Dhillon", "Sidhu Moose Wala", "Karan Aujla", "Shubh",
        "Darshan Raval", "Armaan Malik", "Vishal Mishra", "Sachet Tandon", "Parampara Tandon", "Anuv Jain", "Prateek Kuhad",
        "King", "Lata Mangeshkar", "Kishore Kumar", "Mohammed Rafi", "Asha Bhosle", "Kumar Sanu", "Udit Narayan",
        "Alka Yagnik", "KK", "Shaan", "Mohit Chauhan", "Rahat Fateh Ali Khan", "Nusrat Fateh Ali Khan", "Sunidhi Chauhan",
        "Shankar Mahadevan", "Vishal Shekhar", "Amit Trivedi", "Sachin Jigar", "Tanishk Bagchi", "Mithoon", "Ankit Tiwari",
        "Papon", "Javed Ali", "Mika Singh", "Yo Yo Honey Singh", "Guru Randhawa", "Harrdy Sandhu", "B Praak", "Jasleen Royal",
        "Asees Kaur", "Tulsi Kumar", "Dhvani Bhanushali", "Palak Muchhal", "Sukhwinder Singh", "Kailash Kher",
        "Lucky Ali", "Hariharan", "Sid Sriram", "Anirudh Ravichander", "Devi Sri Prasad", "Thaman S", "Ilaiyaraaja",
        "Ali Sethi", "Shae Gill", "Hasan Raheem", "Talwiinder", "Seedhe Maut", "Divine", "Emiway Bantai", "Raftaar",
        "MC Stan", "Krsna", "Ritviz", "Nucleya", "Jasmine Sandlas", "Gippy Grewal", "Ammy Virk", "Mankirt Aulakh",
        "Jassie Gill", "Akhil", "Shilpa Rao", "Neeti Mohan", "Monali Thakur", "Shalmali Kholgade", "Benny Dayal",
        "Amaal Mallik", "Salim Sulaiman", "Shankar Ehsaan Loy", "Faheem Abdullah", "Hanumankind", "Aditya Rikhari",
    };
}
